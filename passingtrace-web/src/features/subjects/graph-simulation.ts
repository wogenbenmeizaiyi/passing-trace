import type { SubjectGraph } from '@/api/subjects'
import { separateSubjectNodes, type SubjectPosition } from './graph-layout'

export const subjectLinkDistance = 260

export class SubjectGraphSimulation {
  readonly positions: Map<string, SubjectPosition>
  private readonly velocities = new Map<string, SubjectPosition>()
  private readonly links: [string, string][] = []
  private pinnedId: string | undefined
  private alpha = 1
  private quietFrames = 0
  active = false

  constructor(graph: SubjectGraph, positions: Map<string, SubjectPosition>) {
    this.positions = new Map(positions)
    const seen = new Set<string>()
    for (const relation of graph.relations) {
      const a = relation.fromSubjectId,
        b = relation.toSubjectId
      const key = [a, b].sort().join(':')
      if (a !== b && positions.has(a) && positions.has(b) && !seen.has(key)) {
        seen.add(key)
        this.links.push([a, b])
      }
    }
  }

  start() {
    this.active = true
    this.alpha = 1
    this.quietFrames = 0
  }

  pin(id: string) {
    if (!this.positions.has(id)) return
    this.pinnedId = id
    this.velocities.set(id, { x: 0, y: 0 })
    this.start()
  }

  release() {
    this.pinnedId = undefined
    if (this.active) this.start()
  }

  move(id: string, position: SubjectPosition) {
    if (!this.positions.has(id)) return
    this.positions.set(id, { ...position })
    this.velocities.set(id, { x: 0, y: 0 })
    separateSubjectNodes(this.positions, id)
    this.start()
  }

  step(seconds: number) {
    if (!this.active || seconds <= 0) return this.active
    const time = Math.max(0.1, Math.min(2, seconds * 60))
    const before = new Map(this.positions)
    const forces = new Map([...this.positions.keys()].map((id) => [id, { x: 0, y: 0 }]))
    for (const [a, b] of this.links) {
      const from = this.positions.get(a)!,
        to = this.positions.get(b)!
      const dx = to.x - from.x,
        dy = to.y - from.y,
        distance = Math.hypot(dx, dy)
      if (distance < 0.001) continue
      const spring = ((distance - subjectLinkDistance) * 0.045) / distance
      forces.get(a)!.x += dx * spring
      forces.get(a)!.y += dy * spring
      forces.get(b)!.x -= dx * spring
      forces.get(b)!.y -= dy * spring
    }
    const ids = [...this.positions.keys()]
    for (let i = 0; i < ids.length; i++) {
      for (let j = i + 1; j < ids.length; j++) {
        const a = ids[i]!,
          b = ids[j]!
        const from = this.positions.get(a)!,
          to = this.positions.get(b)!
        const dx = to.x - from.x,
          dy = to.y - from.y,
          distance = Math.hypot(dx, dy)
        if (distance < 0.001) continue
        const charge = Math.min(3, 10000 / (distance * distance)) / distance
        forces.get(a)!.x -= dx * charge
        forces.get(a)!.y -= dy * charge
        forces.get(b)!.x += dx * charge
        forces.get(b)!.y += dy * charge
      }
    }
    for (const id of ids) {
      if (id === this.pinnedId) continue
      const previous = this.velocities.get(id) ?? { x: 0, y: 0 },
        force = forces.get(id)!
      let vx = (previous.x + force.x * this.alpha * time) * Math.pow(0.72, time)
      let vy = (previous.y + force.y * this.alpha * time) * Math.pow(0.72, time)
      const speed = Math.hypot(vx, vy)
      if (speed > 14) {
        vx *= 14 / speed
        vy *= 14 / speed
      }
      this.velocities.set(id, { x: vx, y: vy })
      const position = this.positions.get(id)!
      this.positions.set(id, { x: position.x + vx * time, y: position.y + vy * time })
    }
    separateSubjectNodes(this.positions, this.pinnedId ?? '')
    const movement = Math.max(
      0,
      ...ids.map((id) => {
        const current = this.positions.get(id)!,
          old = before.get(id)!
        return Math.hypot(current.x - old.x, current.y - old.y)
      }),
    )
    this.alpha = Math.max(this.pinnedId === undefined ? 0 : 0.15, this.alpha * Math.pow(0.98, time))
    this.quietFrames = movement < 0.06 ? this.quietFrames + 1 : 0
    if (this.quietFrames >= 12) {
      this.active = false
      this.velocities.clear()
    }
    return this.active
  }
}
