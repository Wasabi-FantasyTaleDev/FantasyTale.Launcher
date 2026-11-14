// Included after the build descriptor inside the game adapter's private namespace.
struct ControlLimits { size_t handles, forms, nodes, events; uint32_t text; };

class NativeControls {
    struct Owned { std::string caller, id; uintptr_t form; };
    Descriptor& descriptor_;
    ControlLimits limits_;
    ControlRegistry registry_;
    ControlEvents events_;
    std::recursive_mutex mutex_;
    std::map<uintptr_t, std::string> forms_;
    std::map<uintptr_t, uint64_t> loads_;
    uint64_t load_sequence_ = 0;
    std::atomic<uint64_t> clicks_ = 0;
    std::map<uintptr_t, Owned> owned_;
    std::atomic<bool> available_ = true;
    void* load_original_ = nullptr;
    void* initialize_original_ = nullptr;
    void* destroy_original_ = nullptr;
    void* click_original_ = nullptr;
    static inline NativeControls* active_ = nullptr;

    template<class T> T scalar(uintptr_t address) const {
        T value{};
        if (!read(address, &value, sizeof(value))) throw std::runtime_error("Native control memory is unavailable");
        return value;
    }
    uint32_t offset(const char* key) const { return descriptor_.value(key); }
    uintptr_t address(const char* key) const { return descriptor_.address(key); }
    // Control ABI 2 (kr-1256) keeps text in place, has no enable setter and no verified button factory.
    bool in_place() const { return offset("control_abi") == 2; }
    uint32_t hook_bytes(const char* key, uint32_t fallback) const { return descriptor_.values.contains(key) ? offset(key) : fallback; }
    // Click ABI 2 (cn-406 sub_9812D0) takes no "clicked" byte: every call is an activation.
    bool bare_click() const { return descriptor_.values.contains("click_abi") && offset("click_abi") == 2; }
    uint32_t edit_text_abi() const { return descriptor_.values.contains("edit_text_abi") ? offset("edit_text_abi") : 1; }
    // Control text ABI 2 (cn-324): strings are one pointer to UTF-16 data behind the game's 20-byte header.
    bool legacy_text() const { return descriptor_.values.contains("control_text_abi") && offset("control_text_abi") == 2; }
    std::optional<int32_t> marked_member(const Descriptor::SelectionGroup& group, uintptr_t form) const {
        const auto link = [&](uintptr_t location) { return scalar<uintptr_t>(location); };
        const auto byte = [&](uintptr_t location) { return scalar<uint8_t>(location); };
        const auto integer = [&](uintptr_t location) { return scalar<int32_t>(location); };
        if (group.inline_members)
            return disabled_inline_member(form + group.members_offset, group.inline_layout, offset("enabled_offset"), limits_.nodes, link, byte, integer);
        return disabled_member(scalar<uintptr_t>(form + group.members_offset), group.members, offset("enabled_offset"), limits_.nodes, link, byte, integer);
    }
    std::wstring string_at(uintptr_t location) const {
        if (legacy_text()) return native_calls::legacy_string(scalar<uintptr_t>(location), limits_.text, read);
        const auto length = scalar<uint32_t>(location + 16), capacity = scalar<uint32_t>(location + 20);
        if (length > limits_.text || capacity < length) throw std::length_error("controlTextLimit");
        const auto buffer = capacity < 8 ? location : scalar<uintptr_t>(location);
        std::wstring result(length, L'\0');
        for (uint32_t start = 0; start < length;) {
            const auto count = std::min<uint32_t>(length - start, 2048);
            if (!read(buffer + start * sizeof(wchar_t), result.data() + start, count * sizeof(wchar_t)))
                throw std::runtime_error("Native control text is unavailable");
            start += count;
        }
        return result;
    }
    static std::string utf8(const std::wstring& value) {
        if (value.empty()) return {};
        const auto size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (!size) throw std::runtime_error("Native control text is not valid UTF-16");
        std::string result(static_cast<size_t>(size), '\0');
        if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr))
            throw std::runtime_error("Cannot convert native control text");
        return result;
    }
    std::string control_text(uintptr_t control) const {
        // kr-1256 sub_FB5E70 and kr-1256 sub_FC91C0 store every control's text in one string field.
        if (in_place()) return utf8(string_at(control + offset("text_offset")));
        const auto getter = pointer(pointer(control) + offset("get_text_slot"));
        if (getter != address("control_get_text") && getter != address("edit_get_text")) return {};
        // kr-812 sub_420C30 and kr-812 sub_A9A7A0 return game-allocated strings, including active IME composition.
        // cn-324 sub_4017C0 and cn-324 sub_87EC00 write one string pointer instead.
        alignas(4) uint8_t output[24]{};
        reinterpret_cast<void*(__thiscall*)(void*, void*)>(getter)(reinterpret_cast<void*>(control), output);
        const auto release = [&] { reinterpret_cast<void(__thiscall*)(void*)>(address(legacy_text() ? "string_release" : "control_string_release"))(output); };
        try { auto result = utf8(string_at(reinterpret_cast<uintptr_t>(output))); release(); return result; }
        catch (...) { release(); throw; }
    }
    void retain(uintptr_t control) const { InterlockedIncrement(reinterpret_cast<volatile LONG*>(control + offset("reference_offset"))); }
    void release(uintptr_t control) const {
        if (InterlockedDecrement(reinterpret_cast<volatile LONG*>(control + offset("reference_offset")))) return;
        const auto secondary = control + offset("secondary_offset");
        reinterpret_cast<void(__thiscall*)(void*, int)>(pointer(pointer(secondary) + offset("destructor_slot")))(reinterpret_cast<void*>(secondary), 1);
    }
    struct Reference {
        NativeControls& owner;
        uintptr_t object;
        Reference(NativeControls& adapter, uintptr_t control) : owner(adapter), object(control) { owner.retain(object); }
        ~Reference() { owner.release(object); }
        Reference(const Reference&) = delete;
        Reference& operator=(const Reference&) = delete;
    };
    void reparent(uintptr_t control, uintptr_t parent) const {
        // kr-812 sub_A85440 consumes its by-value intrusive parent reference.
        if (parent) retain(parent);
        reinterpret_cast<void(__thiscall*)(void*, uintptr_t)>(address("control_parent"))(reinterpret_cast<void*>(control), parent);
    }
    void remove_owned(std::map<uintptr_t, Owned>::iterator item) {
        const auto control = item->first, form = item->second.form;
        registry_.invalidate(control);
        owned_.erase(item);
        reparent(control, 0);
        release(control);
        release(form);
    }
    void visit(uintptr_t object, uintptr_t form, const std::string& inherited, std::set<uintptr_t>& seen, std::vector<ControlSnapshot>& result, bool text) {
        struct Pending { uintptr_t object, form; std::string resource; };
        std::vector<Pending> pending{{object, form, inherited}};
        while (!pending.empty()) {
            auto current = std::move(pending.back()); pending.pop_back();
            object = current.object; form = current.form;
            if (!object || !seen.insert(object).second) continue;
            if (seen.size() > limits_.nodes) throw std::length_error("controlTreeLimit");
            if (!scalar<uint8_t>(object + offset("visible_offset"))) continue;
            if (const auto loaded = forms_.find(object); loaded != forms_.end()) { form = object; current.resource = loaded->second; }
            const auto table = pointer(object);
            bool supported = false, edit = false;
            if (in_place()) {
                edit = table == address("edit_vtable");
                supported = edit || table == address("button_vtable") || table == address("label_vtable");
            } else {
                const auto getter = pointer(table + offset("get_text_slot")), setter = pointer(table + offset("text_slot"));
                edit = getter == address("edit_get_text") && setter == address("edit_set_text");
                supported = edit || (getter == address("control_get_text") && setter == address("control_set_text"));
            }
            ControlInfo info;
            info.form = current.resource;
            info.name = utf8(string_at(object + offset("name_offset")));
            info.kind = object == form ? "form" : table == address("button_vtable") ? "button"
                : table == address("edit_vtable") ? "edit" : table == address("label_vtable") ? "label" : "control";
            info.writable = supported;
            info.enabled = scalar<uint8_t>(object + offset("enabled_offset")) != 0;
            const auto array_index = scalar<int32_t>(object + offset("array_index_offset"));
            if (array_index >= 0) info.array_index = array_index;
            if (form && info.array_index) {
                for (const auto& group : descriptor_.selection_groups) {
                    if (group.form != info.form || group.name != info.name || pointer(form) != group.form_vtable) continue;
                    std::optional<int32_t> value;
                    if (group.records.size)
                        value = record_value(scalar<uintptr_t>(form + group.records_offset), scalar<uintptr_t>(form + group.records_offset + 4),
                            group.records, *info.array_index, limits_.nodes, [&](uintptr_t location) { return scalar<int32_t>(location); });
                    else if (static_cast<size_t>(*info.array_index) < group.values.size()) value = group.values[static_cast<size_t>(*info.array_index)];
                    if (!value) continue;
                    info.selection_value = value;
                    if (group.members_offset) {
                        const auto chosen = marked_member(group, form);
                        info.selected = chosen && *chosen == *info.array_index;
                    } else info.selected = scalar<int32_t>(form + group.value_offset) == *value;
                }
            }
            info.text = supported && text ? control_text(object) : std::string{};
            info.x = scalar<int32_t>(object + offset("x_offset")); info.y = scalar<int32_t>(object + offset("y_offset"));
            info.width = scalar<int32_t>(object + offset("width_offset")); info.height = scalar<int32_t>(object + offset("height_offset"));
            if (edit) info.maximum_length = scalar<uint32_t>(object + offset("edit_maximum_offset"));
            result.push_back({object, form, std::move(info)});
            const auto begin = scalar<uintptr_t>(object + offset("children_offset")), end = scalar<uintptr_t>(object + offset("children_offset") + 4);
            if (end < begin || (end - begin) % sizeof(uintptr_t) || (end - begin) / sizeof(uintptr_t) > limits_.nodes)
                throw std::runtime_error("Invalid native control children");
            const auto count = (end - begin) / sizeof(uintptr_t);
            if (count > limits_.nodes - pending.size()) throw std::length_error("controlTreeLimit");
            for (auto cursor = end; cursor > begin; cursor -= sizeof(uintptr_t))
                pending.push_back({scalar<uintptr_t>(cursor - sizeof(uintptr_t)), form, current.resource});
        }
    }
    std::vector<ControlSnapshot> snapshot(bool text = true) {
        std::vector<ControlSnapshot> result;
        std::set<uintptr_t> seen;
        const auto manager = pointer(address("ui_manager"));
        if (!manager) return result;
        // kr-812 sub_A897D0 and kr-812 sub_A89800 expose the form root and default control layer.
        visit(pointer(manager + offset("control_root_offset")), 0, {}, seen, result, text);
        visit(pointer(manager + offset("control_layer_offset")), 0, {}, seen, result, text);
        visit(pointer(manager + offset("control_modal_offset")), 0, {}, seen, result, text);
        for (auto item = owned_.begin(); item != owned_.end();) {
            if (std::none_of(result.begin(), result.end(), [&](const auto& control) { return control.object == item->first; })) {
                const auto expired = item++;
                remove_owned(expired);
            } else ++item;
        }
        registry_.refresh(result);
        return result;
    }
    void record_form(uintptr_t form, std::string path) {
        std::lock_guard guard(mutex_);
        if (forms_.size() >= limits_.forms && !forms_.contains(form)) throw std::length_error("formTrackerLimit");
        std::replace(path.begin(), path.end(), '\\', '/');
        forms_.insert_or_assign(form, std::move(path));
        loads_.insert_or_assign(form, ++load_sequence_);
    }
    static uintptr_t __fastcall load_form(void* form, void*, const void* path) {
        auto* self = active_;
        std::string resource;
        try { resource = utf8(self->string_at(reinterpret_cast<uintptr_t>(path))); }
        catch (...) { self->available_ = false; }
        const auto result = reinterpret_cast<uintptr_t(__thiscall*)(void*, const void*)>(self->load_original_)(form, path);
        if (!resource.empty()) {
            try { self->record_form(reinterpret_cast<uintptr_t>(form), std::move(resource)); }
            catch (...) { self->available_ = false; runtime->log(runtime->context, "native UI: form tracking limit reached"); }
        }
        return result;
    }
    // Returns the initializer's own result: kr-1256 sub_829B30 uses the value kr-1256 sub_FB4D80 returns.
    static uintptr_t __fastcall initialize(void* control, void*) {
        auto* self = active_;
        {
            std::lock_guard guard(self->mutex_);
            const auto object = reinterpret_cast<uintptr_t>(control);
            self->registry_.invalidate(object);
            self->forms_.erase(object);
            self->loads_.erase(object);
        }
        return reinterpret_cast<uintptr_t(__thiscall*)(void*)>(self->initialize_original_)(control);
    }
    static uintptr_t __fastcall destroy(void* control, void*) {
        auto* self = active_;
        {
            std::lock_guard guard(self->mutex_);
            const auto object = reinterpret_cast<uintptr_t>(control);
            self->registry_.invalidate(object);
            self->forms_.erase(object);
            self->loads_.erase(object);
        }
        return reinterpret_cast<uintptr_t(__thiscall*)(void*)>(self->destroy_original_)(control);
    }
    void clicked(void* control) {
        clicks_.fetch_add(1);
        try {
            std::lock_guard guard(mutex_);
            const auto owned = owned_.find(reinterpret_cast<uintptr_t>(control));
            if (owned != owned_.end()) events_.click(owned->second.caller, owned->second.id);
        } catch (...) { available_ = false; }
    }
    static uintptr_t __fastcall click(void* control, void*, uint8_t clicked) {
        auto* self = active_;
        const auto result = reinterpret_cast<uintptr_t(__thiscall*)(void*, uint8_t)>(self->click_original_)(control, clicked);
        if (clicked) self->clicked(control);
        return result;
    }
    static uintptr_t __fastcall activate(void* control, void*) {
        auto* self = active_;
        const auto result = reinterpret_cast<uintptr_t(__thiscall*)(void*)>(self->click_original_)(control);
        self->clicked(control);
        return result;
    }
    void* stage_hook(const char* key, uintptr_t replacement, uint32_t count) {
        auto signature = std::find_if(descriptor_.signatures.begin(), descriptor_.signatures.end(), [&](const auto& item) { return item.first == address(key); });
        if (signature == descriptor_.signatures.end() || signature->second.size() < count) throw std::runtime_error("Missing native control hook signature");
        auto* trampoline = static_cast<uint8_t*>(VirtualAlloc(nullptr, count + 5, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
        if (!trampoline) throw std::runtime_error("Cannot allocate native control trampoline");
        std::memcpy(trampoline, signature->second.data(), count);
        trampoline[count] = 0xE9;
        const auto back = static_cast<uint32_t>(address(key) + count - reinterpret_cast<uintptr_t>(trampoline + count + 5));
        std::memcpy(trampoline + count + 1, &back, 4);
        DWORD previous = 0;
        if (!VirtualProtect(trampoline, count + 5, PAGE_EXECUTE_READ, &previous)) { VirtualFree(trampoline, 0, MEM_RELEASE); throw std::runtime_error("Cannot protect native control trampoline"); }
        FlushInstructionCache(GetCurrentProcess(), trampoline, count + 5);
        std::vector<uint8_t> patch(count, 0x90), mask(count, 255);
        patch[0] = 0xE9;
        const auto relative = static_cast<uint32_t>(replacement - address(key) - 5);
        std::memcpy(patch.data() + 1, &relative, 4);
        if (runtime->stage_patch(runtime->context, address(key), signature->second.data(), mask.data(), patch.data(), count) != FTHOOK_OK) {
            VirtualFree(trampoline, 0, MEM_RELEASE); throw std::runtime_error("Cannot stage native control observer");
        }
        std::copy(patch.begin(), patch.end(), signature->second.begin());
        return trampoline;
    }
public:
    NativeControls(Descriptor& descriptor, ControlLimits limits) : descriptor_(descriptor), limits_(limits), registry_(limits.handles), events_(limits.handles, limits.events) {
        if (!descriptor.matches()) throw std::runtime_error("Native control descriptor does not match this image");
        active_ = this;
        // kr-812 observer prologues contain complete position-independent instructions.
        try {
            load_original_ = stage_hook("control_load_form", reinterpret_cast<uintptr_t>(&load_form), hook_bytes("load_hook_bytes", 7));
            initialize_original_ = stage_hook("control_initialize", reinterpret_cast<uintptr_t>(&initialize), hook_bytes("initialize_hook_bytes", 7));
            destroy_original_ = stage_hook("control_destroy", reinterpret_cast<uintptr_t>(&destroy), hook_bytes("destroy_hook_bytes", 7));
            click_original_ = stage_hook("control_click", bare_click() ? reinterpret_cast<uintptr_t>(&activate) : reinterpret_cast<uintptr_t>(&click),
                hook_bytes("click_hook_bytes", 5));
        } catch (...) {
            if (load_original_) VirtualFree(load_original_, 0, MEM_RELEASE);
            if (initialize_original_) VirtualFree(initialize_original_, 0, MEM_RELEASE);
            if (destroy_original_) VirtualFree(destroy_original_, 0, MEM_RELEASE);
            if (click_original_) VirtualFree(click_original_, 0, MEM_RELEASE);
            active_ = nullptr;
            throw;
        }
    }
    bool available() const { return available_.load(); }
    /// The live forms loaded from one resource. kr-812 sub_86D310 loads Ui/DlgCreateRoom.ui for every new dialog and the
    /// destroy observer forgets it, so a tracked form is exactly an open one; geometry is read without touching the tree.
    std::vector<FormState> forms(const std::string& resource) {
        std::lock_guard guard(mutex_);
        std::vector<FormState> result;
        const auto lower = [](std::string value) {
            std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) { return c >= 'A' && c <= 'Z' ? static_cast<char>(c + ('a' - 'A')) : static_cast<char>(c); });
            return value;
        };
        int32_t root_width = 0, root_height = 0;
        if (const auto manager = pointer(address("ui_manager")))
            if (const auto root = pointer(manager + offset("control_root_offset"))) {
                root_width = scalar<int32_t>(root + offset("width_offset"));
                root_height = scalar<int32_t>(root + offset("height_offset"));
            }
        for (const auto& [form, path] : forms_) {
            if (lower(path) != resource) continue;
            FormState state{path, loads_.contains(form) ? loads_.at(form) : 0};
            state.visible = scalar<uint8_t>(form + offset("visible_offset")) != 0;
            state.x = scalar<int32_t>(form + offset("x_offset")); state.y = scalar<int32_t>(form + offset("y_offset"));
            state.width = scalar<int32_t>(form + offset("width_offset")); state.height = scalar<int32_t>(form + offset("height_offset"));
            state.root_width = root_width; state.root_height = root_height; state.clicks = clicks_.load();
            // The same group value control.inspect compares its selection buttons with, read from the form itself.
            for (const auto& group : descriptor_.selection_groups)
                if (lower(group.form) == resource && pointer(form) == group.form_vtable) {
                    if (!group.members_offset) state.selection = scalar<int32_t>(form + group.value_offset);
                    else if (const auto chosen = marked_member(group, form); chosen && *chosen >= 0 && static_cast<size_t>(*chosen) < group.values.size())
                        state.selection = group.values[static_cast<size_t>(*chosen)];
                    break;
                }
            result.push_back(std::move(state));
        }
        return result;
    }
    bool hooks_installed() const {
        for (const auto* key : {"control_load_form", "control_initialize", "control_destroy", "control_click"})
            if (scalar<uint8_t>(address(key)) == 0xE9) return true;
        return false;
    }
    ~NativeControls() {
        // The runtime has no live unload: only uncommitted activation can release observer code.
        if (load_original_) VirtualFree(load_original_, 0, MEM_RELEASE);
        if (initialize_original_) VirtualFree(initialize_original_, 0, MEM_RELEASE);
        if (destroy_original_) VirtualFree(destroy_original_, 0, MEM_RELEASE);
        if (click_original_) VirtualFree(click_original_, 0, MEM_RELEASE);
        if (active_ == this) active_ = nullptr;
    }
    Result execute(const std::string& caller, const Request& request) {
        std::lock_guard guard(mutex_);
        Result result{request.operation_id, "completed", {}, {}};
        if (!available()) { result.status = "notReady"; result.reason = "controlTrackerUnavailable"; return result; }
        try {
            if (request.kind == "control.events") {
                (void)snapshot(false);
                return events_.poll(caller, request);
            }
            auto tree = snapshot();
            auto selected = registry_.select(caller, request, tree);
            if (selected.empty()) { result.status = "notReady"; result.reason = "controlUnavailable"; return result; }
            if (request.kind == "control.inspect" || selected.size() != 1) {
                result.controls = registry_.describe(caller, selected);
                if (request.kind != "control.inspect") { result.status = "ambiguous"; result.reason = "multipleControls"; }
                return result;
            }
            auto& target = selected.front();
            if (const auto owned = owned_.find(target.object); owned != owned_.end() && owned->second.caller != caller) {
                result.status = "failed"; result.reason = "controlNotOwned"; return result;
            }
            const auto text = wide(request.text);
            if (text.size() > limits_.text || (request.maximum_length && *request.maximum_length > limits_.text)) {
                result.status = "failed"; result.reason = "controlTextLimit"; return result;
            }
            if (request.kind == "control.activate") {
                if (pointer(target.object) != address("button_vtable")) { result.status = "unsupported"; result.reason = "controlActivationUnsupported"; return result; }
                result.controls = registry_.describe(caller, selected);
                const auto activation = button_activation(target.info);
                if (activation == ButtonActivation::already_selected) { result.reason = "alreadySelected"; return result; }
                if (activation != ButtonActivation::ready) { result.status = "notReady"; result.reason = "controlDisabled"; return result; }
                const auto owner = scalar<uintptr_t>(target.object + offset("owner_form_offset"));
                if (owner && std::none_of(tree.begin(), tree.end(), [&](const auto& candidate) { return candidate.object == owner; })) {
                    result.status = "notReady"; result.reason = "controlOwnerUnavailable"; return result;
                }
                std::optional<Reference> keep_form;
                if (owner && owner != target.object) keep_form.emplace(*this, owner);
                const Reference keep_control(*this, target.object);
                // kr-812 sub_A91840 dispatches the native click signal and form command after input validation;
                // cn-406 sub_9812D0 is that dispatch alone.
                if (bare_click()) reinterpret_cast<uintptr_t(__thiscall*)(void*)>(address("control_click"))(reinterpret_cast<void*>(target.object));
                else reinterpret_cast<uintptr_t(__thiscall*)(void*, uint8_t)>(address("control_click"))(reinterpret_cast<void*>(target.object), 1);
                Request current;
                current.kind = "control.inspect"; current.target.id = result.controls.front().id;
                auto observed = registry_.select(caller, current, snapshot());
                if (observed.empty()) {
                    result.controls.clear();
                    if (target.info.selected.has_value()) { result.status = "notReady"; result.reason = "controlNoLongerVisible"; }
                    return result;
                }
                result.controls = registry_.describe(caller, observed);
                if (target.info.selected.has_value() && observed.front().info.selected != true) {
                    result.status = "notReady"; result.reason = "selectionNotApplied";
                }
            } else if (request.kind == "control.setEnabled") {
                if (!in_place() && !descriptor_.addresses.contains("control_set_enabled")) { result.status = "unsupported"; result.reason = "controlEnableUnsupported"; return result; }
                const Reference keep_control(*this, target.object);
                if (in_place()) {
                    // kr-1256 sub_5C9070 inlines the same step: it clears focus through kr-1256 sub_FC04F0(0) when the
                    // control holds it, then writes the enabled byte.
                    const auto manager = pointer(address("ui_manager"));
                    if (manager && pointer(manager + offset("focus_offset")) == target.object)
                        reinterpret_cast<void(__stdcall*)(uintptr_t)>(address("control_focus"))(0);
                    *reinterpret_cast<volatile uint8_t*>(target.object + offset("enabled_offset")) = *request.enabled ? 1 : 0;
                } else {
                    // kr-812 sub_A81270 drops focus from the control when it held it, then writes the enabled byte; the
                    // create-room mode callback kr-812 sub_870ED0 uses it to show which mode button is selected.
                    reinterpret_cast<void(__thiscall*)(void*, uint8_t)>(address("control_set_enabled"))(reinterpret_cast<void*>(target.object), *request.enabled ? 1 : 0);
                }
                // Where the form stores its choice, only the look changes: a re-enabled mode button is still the dialog's
                // selection, so it keeps both its selection value and its selected state. Where the choice is the
                // disabled member (cn-406 sub_770E80), the write changes the selection itself, so it is read again.
                target.info.enabled = *request.enabled;
                if (target.info.selected.has_value() && target.form_object)
                    for (const auto& group : descriptor_.selection_groups)
                        if (group.members_offset && group.form == target.info.form && group.name == target.info.name && pointer(target.form_object) == group.form_vtable) {
                            const auto chosen = marked_member(group, target.form_object);
                            target.info.selected = chosen && target.info.array_index && *chosen == *target.info.array_index;
                        }
                result.controls = registry_.describe(caller, selected);
                result.controls.front().enabled = *request.enabled;
            } else if (request.kind == "control.setText") {
                if (!target.info.writable) { result.status = "unsupported"; result.reason = "controlTextUnsupported"; return result; }
                if (request.maximum_length && !target.info.maximum_length) { result.status = "unsupported"; result.reason = "editRequired"; return result; }
                // kr-812 sub_A9A7A0 inserts kr-812 sub_A9CE10's live composition; do not change the edit underneath it.
                const auto ime = target.info.maximum_length && descriptor_.addresses.contains("ime_manager") ? pointer(address("ime_manager")) : 0;
                if (ime && scalar<uint16_t>(ime + offset("ime_composition_offset"))) {
                    result.status = "notReady"; result.reason = "editCompositionActive"; return result;
                }
                const auto limit = request.maximum_length ? request.maximum_length : target.info.maximum_length;
                if (limit && text.size() > *limit) { result.status = "failed"; result.reason = "textTooLong"; return result; }
                const auto owner = scalar<uintptr_t>(target.object + offset("owner_form_offset"));
                if (owner && std::none_of(tree.begin(), tree.end(), [&](const auto& candidate) { return candidate.object == owner; })) {
                    result.status = "notReady"; result.reason = "controlOwnerUnavailable"; return result;
                }
                result.controls = registry_.describe(caller, selected);
                std::optional<Reference> keep_form;
                if (owner && owner != target.object) keep_form.emplace(*this, owner);
                const Reference keep_control(*this, target.object);
                const BorrowedString value(text);
                if (in_place()) {
                    // kr-1256 sub_5C8330 sets edtRoomTitle's limit by writing the field; kr-1256 sub_FC91C0 then truncates to it.
                    if (request.maximum_length) *reinterpret_cast<volatile int32_t*>(target.object + offset("edit_maximum_offset")) = static_cast<int32_t>(*request.maximum_length);
                    if (pointer(target.object) == address("edit_vtable"))
                        native_calls::set_edit_text(edit_text_abi(), address("edit_set_text"), target.object, value);
                    else reinterpret_cast<void(__thiscall*)(void*, const void*)>(address("control_set_text"))(reinterpret_cast<void*>(target.object), &value);
                } else {
                    const auto setter = pointer(pointer(target.object) + offset("text_slot"));
                    if (request.maximum_length) reinterpret_cast<void(__thiscall*)(void*, int32_t)>(address("edit_set_maximum"))(reinterpret_cast<void*>(target.object), static_cast<int32_t>(*request.maximum_length));
                    if (legacy_text()) {
                        // cn-324 sub_401810 and cn-324 sub_880310 copy from a game string; release ours afterwards.
                        const native_calls::LegacyString game_value(address("string_create"), address("string_release"), text);
                        reinterpret_cast<void(__thiscall*)(void*, const void*)>(setter)(reinterpret_cast<void*>(target.object), game_value.address());
                    } else reinterpret_cast<void(__thiscall*)(void*, const void*)>(setter)(reinterpret_cast<void*>(target.object), &value);
                }
                Request current;
                current.kind = "control.inspect";
                current.target.id = result.controls.front().id;
                if (registry_.select(caller, current, snapshot(false)).empty()) {
                    result.controls.clear(); result.status = "notReady"; result.reason = "controlNoLongerVisible"; return result;
                }
                target.info.text = control_text(target.object);
                if (request.maximum_length) target.info.maximum_length = request.maximum_length;
                if (target.info.text != request.text) { result.status = "failed"; result.reason = "textReadbackMismatch"; }
                target.info.id = result.controls.front().id;
                result.controls.front() = target.info;
            } else if (request.kind == "control.addButton") {
                if (in_place()) { result.status = "unsupported"; result.reason = "controlCreationUnsupported"; return result; }
                const auto& definition = *request.definition;
                if (wide(definition.name).size() > limits_.text) { result.status = "failed"; result.reason = "controlTextLimit"; return result; }
                if (!definition.template_name.empty()) { result.status = "unsupported"; result.reason = "buttonTemplateUnsupported"; return result; }
                if (definition.width > target.info.width || definition.height > target.info.height
                    || definition.x > target.info.width - definition.width || definition.y > target.info.height - definition.height) {
                    result.status = "failed"; result.reason = "controlOutsideForm"; return result;
                }
                if (std::any_of(tree.begin(), tree.end(), [&](const auto& child) { return child.form_object == target.object && child.info.name == definition.name; })) {
                    result.status = "failed"; result.reason = "controlNameConflict"; return result;
                }
                if (owned_.size() >= limits_.handles) { result.status = "busy"; result.reason = "controlHandleLimit"; return result; }
                events_.reserve(caller);
                // kr-812 sub_A927F0 allocates the 760-byte UiButton and supplies the game's default skin.
                const auto control = reinterpret_cast<uintptr_t(__cdecl*)()>(address("control_button_create"))();
                if (!control) { result.status = "failed"; result.reason = "allocationFailed"; return result; }
                retain(control); retain(target.object);
                try {
                    if (pointer(control) != address("button_vtable") || scalar<uint32_t>(control + offset("command_offset")) != 0) throw std::runtime_error("Unexpected native UiButton");
                    const auto name = wide(definition.name);
                    if (legacy_text()) {
                        // cn-324 sub_5B73B0 reads Name at +96; cn-324 sub_401B10 copies one game string into another.
                        const native_calls::LegacyString name_value(address("string_create"), address("string_release"), name),
                            caption(address("string_create"), address("string_release"), text);
                        reinterpret_cast<void(__thiscall*)(void*, const void*)>(address("control_string_assign"))(
                            reinterpret_cast<void*>(control + offset("name_offset")), name_value.address());
                        reinterpret_cast<void(__thiscall*)(void*, const void*)>(address("control_set_text"))(reinterpret_cast<void*>(control), caption.address());
                    } else {
                        const BorrowedString name_value(name), caption(text);
                        // kr-812 sub_A843F0 assigns Name at +240; use the game's string copier.
                        reinterpret_cast<void(__thiscall*)(void*, const void*, uint32_t, uint32_t)>(address("control_string_assign"))(
                            reinterpret_cast<void*>(control + offset("name_offset")), &name_value, 0, UINT32_MAX);
                        reinterpret_cast<void(__thiscall*)(void*, const void*)>(address("control_set_text"))(reinterpret_cast<void*>(control), &caption);
                    }
                    reparent(control, target.object);
                    reinterpret_cast<void(__thiscall*)(void*, int32_t, int32_t, int32_t, int32_t)>(address("control_geometry"))(
                        reinterpret_cast<void*>(control), definition.x, definition.y, definition.width, definition.height);
                    ControlSnapshot added{control, target.object, {}};
                    added.info = {{}, target.info.form, definition.name, "button", request.text, true, definition.x, definition.y, definition.width, definition.height, {}};
                    added.info.enabled = true;
                    result.controls = registry_.describe(caller, {added});
                    owned_.emplace(control, Owned{caller, result.controls.front().id, target.object});
                } catch (...) { registry_.invalidate(control); reparent(control, 0); release(control); release(target.object); throw; }
            } else if (request.kind == "control.remove") {
                const auto owned = owned_.find(target.object);
                if (owned == owned_.end()) { result.status = "failed"; result.reason = "controlNotOwned"; return result; }
                remove_owned(owned);
            }
        } catch (const std::length_error& error) { result.status = "busy"; result.reason = error.what(); }
        return result;
    }
};
