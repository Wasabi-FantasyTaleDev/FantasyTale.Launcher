import { localized } from '../i18n'
import { selectedBuild, selectedGame, selectedInstance, selectedServer } from '../state'
import { startUpdatePreview } from './preview'

export function previewCurrentClient() {
  const game = selectedGame.value, build = selectedBuild.value, instance = selectedInstance.value
  startUpdatePreview({ kind: 'client', gameId: game?.id, buildId: build?.id, instanceId: instance?.id,
    serverId: selectedServer.value?.id, name: `${localized(game?.name)}${build ? ` · ${localized(build.name)}` : ''}`,
    fromVersion: '1.0.0', toVersion: '1.0.1', sourceName: 'Fantasy Tale', sourceKind: 'alist',
    directory: instance?.installationPath ?? `D:\\Games\\FantasyTale\\${game?.id ?? 'client'}\\${build?.id ?? 'default'}` })
}
