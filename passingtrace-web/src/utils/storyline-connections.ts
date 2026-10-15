/** Return a user-facing reason without changing the graph. */
export function connectionProblem(
  source: string,
  target: string,
  nodes: readonly { id: string }[],
  edges: readonly { source: string; target: string }[],
): string | null {
  if (!nodes.some((node) => node.id === source) || !nodes.some((node) => node.id === target)) {
    return '节点已移除，请重新选择连接点。'
  }
  if (source === target) return '不能连接到节点自己，请选择另一个节点。'
  if (edges.some((edge) => edge.source === source && edge.target === target)) {
    return '这两个节点已经连接了。'
  }
  const successors = new Map<string, string[]>()
  for (const edge of edges) {
    const targets = successors.get(edge.source) ?? []
    targets.push(edge.target)
    successors.set(edge.source, targets)
  }
  const pending = [target]
  const visited = new Set<string>()
  while (pending.length) {
    const id = pending.pop()!
    if (id === source) return '这样会形成循环，请选择后续节点。'
    if (visited.has(id)) continue
    visited.add(id)
    pending.push(...(successors.get(id) ?? []))
  }
  return null
}
