import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { call, type BuildSummary, type ClientComponent } from '../bridge'
import { trackDownload } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import { previewInstallationComponents } from './clientInstallPreview'

type InstallContext = { open: boolean; game: string; build?: BuildSummary; serverId?: string; nativeLanguage?: string; preview?: boolean }

/** Keeps installation consent scoped to the displayed build, server and target client. */
export function useClientInstallComponents(context: () => InstallContext) {
  const items = ref<ClientComponent[]>([]), selected = ref<string[]>([]), languages = ref<string[]>([])
  const loading = ref(false), error = ref(''), tasks = ref<DownloadTask[]>([])
  const current = computed(context)
  const nativeLanguage = computed(() => current.value.build?.nativeLanguage || current.value.nativeLanguage || '')
  const additionalLanguages = computed(() => Object.keys(current.value.build?.additionalLanguages ?? {}).filter(code => code !== nativeLanguage.value))
  let revision = 0
  onBeforeUnmount(() => { revision++ })
  async function refresh() {
    const request = ++revision, snapshot = current.value
    tasks.value = []; items.value = []; selected.value = []; languages.value = nativeLanguage.value ? [nativeLanguage.value] : []; error.value = ''; loading.value = false
    if (!snapshot.open || !snapshot.game || !snapshot.build) return
    loading.value = true
    try {
      const result = snapshot.preview ? previewInstallationComponents() : await call('plugins.installationPlan', { game: snapshot.game, build: snapshot.build.id, serverId: snapshot.serverId || undefined })
      if (request !== revision) return
      items.value = result; selected.value = result.filter(item => item.required || item.defaultSelected === true).map(item => item.id)
    } catch (cause) { if (request === revision) error.value = (cause as Error).message }
    finally { if (request === revision) loading.value = false }
  }
  watch(current, refresh, { immediate: true })
  function selectPlugin(id: string, checked: boolean) {
    if (!items.value.some(item => item.id === id) || items.value.some(item => item.id === id && item.required)) return
    selected.value = checked ? [...new Set([...selected.value, id])] : selected.value.filter(value => value !== id)
  }
  function selectLanguage(code: string, checked: boolean) {
    if (!additionalLanguages.value.includes(code)) return
    languages.value = checked ? [...new Set([...languages.value, code])] : languages.value.filter(value => value !== code)
  }
  function confirm() {
    if (loading.value || error.value || !current.value.open || !current.value.build) throw new Error(error.value || 'The installation choices are not ready.')
    const request = revision
    const serverId = current.value.serverId || undefined
    const ids = [...new Set([...selected.value, ...items.value.filter(item => item.required).map(item => item.id)])], chosenLanguages = [...languages.value]
    return async (instanceId: string) => {
      const admitted = await call('plugins.installationComponents', { instanceId, serverId, ids, languages: chosenLanguages })
      admitted.forEach(trackDownload)
      if (request === revision && current.value.open) tasks.value = admitted
      return admitted
    }
  }
  return { tasks, items, selected, languages, nativeLanguage, additionalLanguages, loading, error, refresh, selectPlugin, selectLanguage, confirm }
}
