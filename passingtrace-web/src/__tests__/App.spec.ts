import { describe, expect, it } from 'vitest'

import router from '@/router'

describe('PassingTrace web routes', () => {
  it('根地址与未知地址都直接回到记录列表', () => {
    const routes = router.getRoutes()
    expect(routes.find((route) => route.path === '/')?.redirect).toBe('/events')
    expect(routes.find((route) => route.path === '/:pathMatch(.*)*')?.redirect).toBe('/events')
  })
})
