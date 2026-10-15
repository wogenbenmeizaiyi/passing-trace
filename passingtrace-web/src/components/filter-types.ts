export type FilterValues = Record<string, string | string[]>
export interface FilterField {
  key: string
  label: string
  type?: 'date' | 'tags'
  options?: Array<{ value: string; label: string }>
  defaultValue?: string
  ownOnly?: boolean
  unavailable?: boolean
}
export function dateBoundary(value: string, end = false) {
  return value ? new Date(`${value}T${end ? '23:59:59.999' : '00:00:00'}`).toISOString() : undefined
}
