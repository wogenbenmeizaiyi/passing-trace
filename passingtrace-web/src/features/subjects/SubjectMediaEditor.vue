<script setup lang="ts">
import { ref } from 'vue'
import { mediaApi } from '@/api/media'
const model = defineModel<string[]>({ default: () => [] })
const busy = ref(false),
  error = ref(''),
  progress = ref(0)
async function upload(event: Event) {
  const files = (event.target as HTMLInputElement).files
  if (!files?.length || busy.value) return
  busy.value = true
  error.value = ''
  try {
    for (const file of files) {
      if (model.value.length >= 10) throw new Error('最多保存 10 个媒体附件')
      const actual = file.name.toLowerCase().endsWith('.glb')
        ? new File([file], file.name, { type: 'model/gltf-binary' })
        : file
      const result = await mediaApi.upload(actual, (x) => (progress.value = x.percent))
      model.value = [...model.value, result.id]
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    busy.value = false
    ;(event.target as HTMLInputElement).value = ''
  }
}
defineExpose({ busy })
</script>
<template>
  <fieldset class="subject-panel">
    <legend>图片、视频与三维模型</legend>
    <p>GLB 模型需内嵌贴图；最多 10 个附件。</p>
    <input
      type="file"
      multiple
      accept="image/jpeg,image/png,image/webp,video/mp4,video/webm,video/quicktime,.glb"
      aria-label="上传人物媒体"
      :disabled="busy"
      @change="upload"
    />
    <p v-if="busy" role="status">正在上传 {{ progress }}%</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <ol>
      <li v-for="(id, index) in model" :key="id">
        附件 {{ index + 1 }}
        <button type="button" @click="model = model.filter((x) => x !== id)">移除</button>
      </li>
    </ol>
  </fieldset>
</template>
