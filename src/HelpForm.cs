using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnimeRenamer
{
    public class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "使用说明";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 650);
            MinimumSize = new Size(620, 540);
            BackColor = Color.White;
            Font = Theme.F(9f);
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Color.White;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(root);

            var title = Ui.Label("使用说明", 15f, Theme.Text, FontStyle.Bold);
            title.Dock = DockStyle.Fill;
            title.Padding = new Padding(24, 16, 0, 0);
            root.Controls.Add(title, 0, 0);

            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Color.White;
            box.ForeColor = Theme.Text;
            box.Font = Theme.F(9.25f);
            box.ScrollBars = ScrollBars.Vertical;
            box.WordWrap = true;
            box.Dock = DockStyle.Fill;
            box.Text = Content.Replace("\r\n", "\n").Replace("\n", "\r\n");
            box.Select(0, 0);

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Color.White;
            host.Padding = new Padding(24, 0, 18, 6);
            host.Controls.Add(box);
            root.Controls.Add(host, 0, 1);

            var bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Color.White;
            root.Controls.Add(bottom, 0, 2);

            var close = new FlatButton();
            close.Text = "知道了";
            close.Kind = ButtonKind.Primary;
            close.Size = new Size(100, 34);
            close.Location = new Point(ClientSize.Width - 124, 8);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;
            bottom.Controls.Add(close);
            AcceptButton = close;
        }

        private const string Content =
@"【三步完成】

1. 添加番剧文件夹
   · 点击「添加文件夹」，或直接把文件夹拖进窗口（支持一次拖入多个）。
   · 勾选「包含子文件夹」可扫描 Season 1、Season 2 等子目录。

2. 确认番剧匹配
   · 添加后会自动用目录名到 Bangumi 搜索并匹配。
   · 匹配不对时，修改「关键词」后点「搜索」，在「匹配」下拉里选择正确条目。
   · 「标题」可以直接手动修改；「季数」用于决定 Sxx 编号。
   · 「对齐 01」：文件是连续编号（例如 25-36）时，一键设置偏移，让第一集变成 01。

3. 检查预览并执行
   · 绿色「就绪」表示会改名；红色「冲突」不会执行（例如目标文件已存在）。
   · 点击行首复选框，可以取消某一行。
   · 确认无误后点「执行重命名」，完成后可点「撤销上次」恢复。


【命名模板】

模板中可以使用的占位符：

  {title}       当前标题（可在「标题」中修改）
  {title_cn}    匹配条目的中文名
  {title_orig}  匹配条目的原名
  {season}      季数，{season:02d} 表示两位数（01）
  {ep}          集数，{ep:02d} 表示两位数（05）
  {ep_title}    单集标题（来自 Bangumi / TMDB，可能为空）
  {year}        年份
  {resolution}  分辨率（1080p / 2160p 等）
  {group}       发布组（文件名开头的方括号内容）

常用模板：

  {title} - S{season:02d}E{ep:02d}                 Emby / Jellyfin 标准
  {title} - S{season:02d}E{ep:02d} - {ep_title}    附带单集标题
  {title} 第{ep:02d}话                              中文话数风格
  {title} - {ep:02d}                               简洁集数风格


【识别规则】

· 支持 [01]、- 01、E01、EP01、第01话、S01E01、01v2 等常见写法。
· 文件名中带 SxxExx 时，优先使用文件内的季数；否则使用界面上设置的「季数」。
· 特别篇（SP / OVA / OAD）输出为 S00E01 这样的编号。
· 片头、片尾、预告（NCOP / NCED / PV / CM / Menu / Sample）默认跳过。
· 字幕按集数自动配对，并保留 .chs / .cht / .jpn 等语言后缀。
· .nfo、缩略图等附属文件会跟随视频一起改名。


【安全说明】

· 本工具只修改文件名，不移动文件夹、不修改文件内容。
· 目标文件已存在或新名称重复时，该行会标记为「冲突」并跳过，绝不覆盖。
· 每次执行都会记录撤销历史（设置中的数据目录 / history），可随时「撤销上次」。
· 建议先在小文件夹上试用，确认模板和识别结果符合预期。


【数据源】

· Bangumi 番组计划：无需 API Key，推荐使用，可获取中文标题与单集标题。
· TMDB：需要在「设置」中填写 API Key。国内网络通常需要填写代理。
· 仅本地：完全不联网，用文件夹名作为标题、按识别出的集数重命名。


【快捷键】

· Ctrl+O  添加文件夹
· F5      刷新预览
· F1      打开本说明
· Ctrl+,  打开设置


【常见问题】

· 识别不到集数：文件名里的数字可能被当成年份或分辨率而忽略。可以在预览中排除，
  或先把文件改成带 [01]、- 01 的格式。
· 字幕没有跟着改：确认「重命名字幕」已勾选，并且字幕文件名里包含集数。
· 匹配到错误的季度：选择正确的条目，或手动修改标题与「季数」。
· 杀毒软件提示：本程序为单文件绿色程序，不会修改系统设置；如有误报请添加信任。
";
    }
}
