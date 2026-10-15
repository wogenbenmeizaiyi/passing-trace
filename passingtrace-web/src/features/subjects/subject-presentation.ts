import type { Subject, SubjectValue } from '@/api/subjects'

export function subjectType(subject: Subject) {
  if (subject.kind === 0) return '人'
  if (subject.kind === 1) return '宠物'
  return (
    (
      { vehicle: '车辆', property: '房屋', bicycle: '自行车', collectible: '收藏品' } as Record<
        string,
        string
      >
    )[subject.itemType ?? ''] ?? '物品'
  )
}

export function subjectValue(value: SubjectValue | undefined) {
  return value == null
    ? '未填写'
    : typeof value === 'boolean'
      ? value
        ? '是'
        : '否'
      : String(value)
}
