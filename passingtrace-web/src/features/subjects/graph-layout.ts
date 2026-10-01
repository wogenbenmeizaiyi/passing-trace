import type { SubjectGraph } from '@/api/subjects'

export type SubjectPosition = { x: number; y: number }
export const subjectNodeSpacing = 184

export function separateSubjectNodes(positions: Map<string, SubjectPosition>, pinnedId: string) {
  const ids = [...positions.keys()].sort()
  for (let pass = 0; pass < 80; pass++) {
    let largestOverlap = 0
    for (let i = 0; i < ids.length; i++) {
      for (let j = i + 1; j < ids.length; j++) {
        const aId = ids[i]!,
          bId = ids[j]!
        const a = positions.get(aId)!,
          b = positions.get(bId)!
        const dx = b.x - a.x,
          dy = b.y - a.y,
          distance = Math.hypot(dx, dy)
        const overlap = subjectNodeSpacing - distance
        if (overlap <= 0.01) continue
        largestOverlap = Math.max(largestOverlap, overlap)
        const angle = (i + j * ids.length) * 2.399963229728653
        const ux = distance > 0.0001 ? dx / distance : Math.cos(angle)
        const uy = distance > 0.0001 ? dy / distance : Math.sin(angle)
        const push = overlap + 0.02
        if (aId === pinnedId) {
          positions.set(bId, { x: b.x + ux * push, y: b.y + uy * push })
        } else if (bId === pinnedId) {
          positions.set(aId, { x: a.x - ux * push, y: a.y - uy * push })
        } else {
          positions.set(aId, { x: a.x - (ux * push) / 2, y: a.y - (uy * push) / 2 })
          positions.set(bId, { x: b.x + (ux * push) / 2, y: b.y + (uy * push) / 2 })
        }
      }
    }
    if (largestOverlap <= 0.01) break
  }
}

export function subjectLayout(graph: SubjectGraph) {
  const adjacent = new Map(graph.nodes.map((x) => [x.id, [] as string[]]))
  for (const r of graph.relations) {
    adjacent.get(r.fromSubjectId)?.push(r.toSubjectId)
    adjacent.get(r.toSubjectId)?.push(r.fromSubjectId)
  }
  const depth = new Map([[graph.rootId, 0]])
  const parent = new Map<string, string>()
  const queue = [graph.rootId]
  for (let i = 0; i < queue.length; i++) {
    for (const next of adjacent.get(queue[i]!) ?? [])
      if (!depth.has(next)) {
        depth.set(next, depth.get(queue[i]!)! + 1)
        parent.set(next, queue[i]!)
        queue.push(next)
      }
  }
  const positions = new Map<string, { x: number; y: number }>([[graph.rootId, { x: 0, y: 0 }]])
  let radius = 0
  for (let level = 1; level <= Math.max(0, ...depth.values()); level++) {
    const ring = graph.nodes
      .filter((x) => depth.get(x.id) === level)
      .sort((a, b) => a.id.localeCompare(b.id))
    radius = Math.max(radius + 260, (ring.length * 260) / (2 * Math.PI))
    ring.forEach((node, i) => {
      const angle = (2 * Math.PI * i) / ring.length - Math.PI / 2
      positions.set(node.id, { x: Math.cos(angle) * radius, y: Math.sin(angle) * radius })
    })
  }
  return { positions, parent }
}

export function connectedFilter(
  graph: SubjectGraph,
  query: string,
  kind: string,
  selfName?: string,
) {
  const matches = new Set(
    graph.nodes
      .filter(
        (x) =>
          (x.isSelf && selfName ? selfName : x.name).includes(query.trim()) &&
          (!kind || String(x.kind) === kind),
      )
      .map((x) => x.id),
  )
  const kept = new Set(matches)
  kept.add(graph.rootId)
  const { parent } = subjectLayout(graph)
  for (const id of matches) {
    let node: string | undefined = id
    while ((node = parent.get(node))) kept.add(node)
  }
  return { matches, kept }
}
