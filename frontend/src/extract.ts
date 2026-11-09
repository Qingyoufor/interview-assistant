/**
 * 把上传的文件转成纯文本。
 * - PDF：pdfjs 逐页抽文字（扫描件没有文字层，会得到空串，需要提示用户改用图片/粘贴）
 * - 图片：tesseract OCR（中文 + 英文），第一次跑会下载语言包，比较慢
 * 两个库都是按需 import，不进首屏 bundle。
 */
export async function extractText(file: File): Promise<string> {
  const name = file.name.toLowerCase()

  if (file.type === 'application/pdf' || name.endsWith('.pdf')) return extractPdf(file)
  if (file.type.startsWith('image/')) return extractImage(file)

  throw new Error('只支持 PDF 或图片（png / jpg / webp）')
}

async function extractPdf(file: File): Promise<string> {
  const pdfjs = await import('pdfjs-dist')
  const workerUrl = (await import('pdfjs-dist/build/pdf.worker.min.mjs?url')).default
  pdfjs.GlobalWorkerOptions.workerSrc = workerUrl

  const pdf = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise
  const pages: string[] = []

  for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
    const page = await pdf.getPage(pageNumber)
    const content = await page.getTextContent()
    const line = content.items.map((item) => ('str' in item ? item.str : '')).join(' ')
    pages.push(line)
  }

  return pages.join('\n').trim()
}

async function extractImage(file: File): Promise<string> {
  const { createWorker } = await import('tesseract.js')
  // 用 'a+b' 字符串形式而不是数组：数组形式在 tesseract.js v7 里会多传一个坏语言名
  const worker = await createWorker('chi_sim+eng')

  try {
    const result = await worker.recognize(file)
    return result.data.text.trim()
  } catch {
    // tesseract 的 worker / wasm core / 语言包都默认从 CDN 拉，断网或 CDN 不通时会走到这里
    throw new Error('图片识别失败：OCR 引擎和中文语言包需要联网下载，请检查网络后重试，或直接粘贴文本。')
  } finally {
    await worker.terminate()
  }
}
