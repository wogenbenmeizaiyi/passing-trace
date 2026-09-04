import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import AmapActionCards from '@/components/AmapActionCards.vue'

describe('高德 AI 动作卡片', () => {
  afterEach(() => vi.restoreAllMocks())

  it('只用固定模板生成导航地址', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    const wrapper = mount(AmapActionCards, {
      props: {
        actions: [
          {
            type: 'amap-navigation',
            provider: 'amap',
            label: '导航到人民广场地铁站',
            placeName: '人民广场地铁站',
            address: '上海市黄浦区',
            latitude: 31.232,
            longitude: 121.475,
            coordinateSystem: 'GCJ02',
            poiId: 'p1',
            source: 'amap-live',
            webUrl: 'https://evil.example/navigation',
          },
        ],
      },
    })

    await wrapper.get('button').trigger('click')

    const target = new URL(open.mock.calls[0]![0] as string)
    expect(target.hostname).toBe('uri.amap.com')
    expect(target.searchParams.get('to')).toBe('121.475,31.232,人民广场地铁站')
    expect(open.mock.calls[0]![2]).toBe('noopener,noreferrer')
  })

  it('拒绝恶意地图链接并展示最多三个被动候选', () => {
    const places = Array.from({ length: 4 }, (_, index) => ({
      candidateId: `candidate-${index}`,
      poiId: `poi-${index}`,
      name: `候选地点 ${index + 1}`,
      address: `测试路 ${index + 1} 号`,
      province: '上海市',
      city: '上海市',
      district: '黄浦区',
      latitude: 31.2 + index / 100,
      longitude: 121.4 + index / 100,
      coordinateSystem: 'GCJ02' as const,
      source: 'amap-live' as const,
    }))
    const wrapper = mount(AmapActionCards, {
      props: {
        places,
        actions: [
          {
            type: 'amap-trip-map',
            provider: 'amap',
            label: '打开行程地图',
            placeName: '杭州一天',
            address: null,
            latitude: 0,
            longitude: 0,
            coordinateSystem: 'GCJ02',
            source: 'amap-live',
            poiId: null,
            webUrl: 'https://evil.example/trip',
          },
        ],
      },
    })

    expect(wrapper.findAll('button')).toHaveLength(0)
    expect(wrapper.findAll('.amap-card--passive')).toHaveLength(3)
  })
})
