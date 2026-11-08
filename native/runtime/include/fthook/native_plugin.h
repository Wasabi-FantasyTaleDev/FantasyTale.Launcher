#pragma once
#include "engine.h"
#include <memory>

namespace fthook {
/// Owns a native activation attempt. Failure removes its locators and invokes cleanup
/// before unloading the DLL. Successful instances are retained through process exit.
class NativePlugin {
public:
    /// Loads a local DLL and calls its ABI entry after manifest and option validation.
    static std::unique_ptr<NativePlugin> start(PreparedPlugin& plugin, const SessionPlugin& entry,
        const Session& session, LocatorRegistry& locators, std::string& error);
    /// Rolls back an uncommitted activation; successful plugins have process lifetime.
    ~NativePlugin();
    /// Retains the instance and API storage through process exit. Call only after all
    /// its patches have been applied. At most 64 instances can exist in this runtime.
    /// Passing false hides the locators of a failed but quarantined activation.
    static void retain(std::unique_ptr<NativePlugin> plugin, bool expose_locators = true);
    NativePlugin(const NativePlugin&) = delete;
    NativePlugin& operator=(const NativePlugin&) = delete;
private:
    struct State;
    std::unique_ptr<State> state_;
    explicit NativePlugin(std::unique_ptr<State> state);
};
}
