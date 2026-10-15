import type { Friend } from '@/api/social'

export function friendGroups(friends: Friend[], query: string) {
  const search = query.trim().toLocaleLowerCase()
  const groups = new Map<string, Friend[]>()
  for (const friend of friends) {
    if (
      ![friend.person.nickname, friend.remark, friend.label, friend.relationship ?? ''].some(
        (value) => value.toLocaleLowerCase().includes(search),
      )
    )
      continue
    const label = friend.label.trim() || '朋友'
    const group = groups.get(label) ?? []
    group.push(friend)
    groups.set(label, group)
  }
  return [...groups]
    .sort(([a], [b]) => a.localeCompare(b, 'zh-CN'))
    .map(([label, items]) => ({
      label,
      items: items.sort((a, b) =>
        (a.remark || a.person.nickname).localeCompare(b.remark || b.person.nickname, 'zh-CN'),
      ),
    }))
}

export function showMessageTime(current: string, previous?: string) {
  const date = new Date(current),
    before = previous ? new Date(previous) : null
  return (
    !before ||
    date.toDateString() !== before.toDateString() ||
    date.getTime() - before.getTime() > 300000
  )
}
export function socialTime(value: string, full = false) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleString('zh-CN', {
    ...(full || date.toDateString() !== new Date().toDateString()
      ? ({ month: 'numeric', day: 'numeric' } as const)
      : {}),
    hour: '2-digit',
    minute: '2-digit',
  })
}
