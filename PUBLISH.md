# 发布到 GitHub

这个目录已经整理成可直接上传的仓库内容。

## 一、新建仓库

在 GitHub 新建仓库，例如 `AnimeRenamer`：

- 不要勾选 “Add a README file”
- 不要勾选 “Add .gitignore”
- 不要勾选 “Choose a license”

因为本目录里已经包含这些文件。

## 二、上传方式

### 方式 A：网页上传

1. 解压 `AnimeRenamer-v1.0.7-github.zip`
2. 打开解压后的 `AnimeRenamer` 文件夹
3. 在 GitHub 仓库页面点击 **Add file → Upload files**
4. 把文件夹里的**所有内容**拖进去（不是把外层文件夹拖进去）
5. 提交

### 方式 B：命令行

```bash
cd AnimeRenamer
git init
git add .
git commit -m "Initial release v1.0.7"
git branch -M main
git remote add origin https://github.com/<你的用户名>/AnimeRenamer.git
git push -u origin main
```

## 三、发布 Release

1. 仓库页面右侧 **Releases → Draft a new release**
2. Tag 填 `v1.0.7`
3. 标题填 `AnimeRenamer v1.0.7`
4. 上传附件：`AnimeRenamer_v1.0.7_windows.zip`
5. 发布

这个 zip 里包含 EXE 和使用说明，普通用户下载解压后双击即可。

## 四、建议填写的仓库信息

**Description**

```text
番剧批量重命名：Bangumi/TMDB 匹配、字幕同步、预览与撤销，Windows 单文件 EXE。
```

**Topics**

```text
anime, bangumi, tmdb, emby, jellyfin, renamer, winforms, csharp, windows
```

## 五、注意事项

- 程序没有代码签名，用户首次运行可能看到 SmartScreen，点“更多信息 → 仍要运行”即可，README 里已说明。
- 不要提交本机的 `build/`、`dist/AnimeRenamer-data/`、日志或测试目录，`.gitignore` 已处理。
- 如果以后更新版本，修改 `src/Program.cs` 里的 `VersionText` 和 `src/AssemblyInfo.cs`，重新构建后替换 `dist/AnimeRenamer.exe`。
