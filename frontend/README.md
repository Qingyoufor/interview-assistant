# Interview Coach · 前端

Vue 3 + TypeScript + Vite + naive-ui。五个页面：资料（`/resumes`）、匹配（`/match`）、历史（`/history`）、练习（`/practice/:sessionId`）、报告（`/report/:sessionId`）。

## 跑起来

后端先起在 `http://localhost:5052`（`dotnet run --project backend/InterviewAssistant.Api --launch-profile http`），然后：

```bash
npm install
npm run dev      # http://localhost:5173，/api 由 Vite 代理到后端
```

其它命令：

```bash
npm run build    # vue-tsc 类型检查 + vite build
npm run preview  # 预览构建产物
```

## 录入方式

资料页支持三种录入：

| 方式 | 实现 | 说明 |
|---|---|---|
| 粘贴文本 | —— | 最快，也最准 |
| PDF | `pdfjs-dist` 在浏览器抽文字层 | 扫描版 PDF 没有文字层，会提示改用图片 |
| 图片 | `tesseract.js` OCR（`chi_sim+eng`） | 首次运行从 CDN 下载 worker、wasm core 和语言包（约 20 MB 起），识别一页要十几秒；**断网时不可用**，会给出提示让改粘贴 |

两个库都是按需 `import`（`src/extract.ts`），不进首屏 bundle；解析出来的文字填进输入框，确认后再点「解析并保存」——后端始终只收纯文本。

## 约定

- 接口只在 `src/api.ts` 里出现，组件不直接 `fetch`。
- 后端错误统一是 `{ error }`，`ApiError` 把状态码和文案一起抛出，页面显示在顶部 `NAlert`；连不上后端时给的是「请确认 API 已在 http://localhost:5052 启动」。
- 不引入图片资源：仓库规则是图片/音视频不进 Git，界面只靠 CSS 和文字。
- 路由级懒加载（`router.ts`），四个页面各自一个 chunk。
