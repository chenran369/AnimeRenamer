using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AnimeRenamer
{
    public class SettingsForm : Form
    {
        private readonly string _dataDir;
        private TextBox _txtKey;
        private TextBox _txtProxy;
        private Label _lblTest;
        private ComboBox _cboLanguage;

        public SettingsForm(Config config, string dataDir)
        {
            _dataDir = dataDir;
            Text = "设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 452);
            MinimumSize = new Size(560, 452);
            MaximumSize = new Size(760, 540);
            BackColor = Color.White;
            Font = Theme.F(9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            BuildUi(config);
        }

        private void BuildUi(Config config)
        {
            var title = Ui.Label("设置", 15f, Theme.Text, FontStyle.Bold);
            title.Location = new Point(24, 18);
            title.Size = new Size(300, 30);
            Controls.Add(title);

            var sub = Ui.Label("TMDB 需要 API Key；Bangumi 无需配置即可使用。", 8.75f, Theme.Muted, FontStyle.Regular);
            sub.Location = new Point(25, 48);
            sub.Size = new Size(500, 20);
            Controls.Add(sub);

            Label keyLabel = Ui.Caption("TMDB API Key");
            keyLabel.Location = new Point(24, 82);
            keyLabel.Size = new Size(130, 22);
            Controls.Add(keyLabel);

            _txtKey = Ui.TextBox();
            _txtKey.Location = new Point(24, 104);
            _txtKey.Size = new Size(512, 28);
            _txtKey.Text = config.TmdbKey ?? "";
            _txtKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_txtKey);

            var keyHint = Ui.Label("在 themoviedb.org/settings/api 免费申请（v3 Key 或 v4 Token 均可）", 8.25f, Theme.Faint, FontStyle.Regular);
            keyHint.Location = new Point(24, 136);
            keyHint.Size = new Size(512, 18);
            Controls.Add(keyHint);

            Label proxyLabel = Ui.Caption("网络代理（可选）");
            proxyLabel.Location = new Point(24, 168);
            proxyLabel.Size = new Size(160, 22);
            Controls.Add(proxyLabel);

            _txtProxy = Ui.TextBox();
            _txtProxy.Location = new Point(24, 190);
            _txtProxy.Size = new Size(300, 28);
            _txtProxy.Text = config.Proxy ?? "";
            Controls.Add(_txtProxy);

            var proxyHint = Ui.Label("例如 http://127.0.0.1:7890，留空表示直连", 8.25f, Theme.Faint, FontStyle.Regular);
            proxyHint.Location = new Point(336, 195);
            proxyHint.Size = new Size(210, 18);
            Controls.Add(proxyHint);

            Label langLabel = Ui.Caption("标题语言");
            langLabel.Location = new Point(24, 232);
            langLabel.Size = new Size(80, 22);
            Controls.Add(langLabel);

            _cboLanguage = Ui.Combo(false);
            _cboLanguage.Items.AddRange(new object[] { "中文标题（推荐）", "日文原名" });
            _cboLanguage.SelectedIndex = config.Language == "orig" ? 1 : 0;
            _cboLanguage.Location = new Point(104, 228);
            _cboLanguage.Size = new Size(180, 28);
            Controls.Add(_cboLanguage);

            Label dataLabel = Ui.Caption("数据目录");
            dataLabel.Location = new Point(24, 274);
            dataLabel.Size = new Size(70, 22);
            Controls.Add(dataLabel);

            var dataValue = Ui.Label(_dataDir, 8.5f, Theme.Muted, FontStyle.Regular);
            dataValue.Location = new Point(96, 274);
            dataValue.Size = new Size(340, 22);
            dataValue.AutoEllipsis = true;
            Controls.Add(dataValue);

            var openData = new FlatButton();
            openData.Text = "打开";
            openData.Kind = ButtonKind.Secondary;
            openData.Size = new Size(64, 28);
            openData.Location = new Point(448, 270);
            openData.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + _dataDir + "\""); }
                catch { }
            };
            Controls.Add(openData);

            _lblTest = Ui.Label("", 8.5f, Theme.Muted, FontStyle.Regular);
            _lblTest.Location = new Point(24, 312);
            _lblTest.Size = new Size(512, 22);
            _lblTest.AutoEllipsis = true;
            Controls.Add(_lblTest);

            var testBgm = new FlatButton();
            testBgm.Text = "测试 Bangumi";
            testBgm.Kind = ButtonKind.Soft;
            testBgm.Size = new Size(120, 32);
            testBgm.Location = new Point(24, 340);
            testBgm.Click += async delegate { await Test("bangumi"); };
            Controls.Add(testBgm);

            var testTmdb = new FlatButton();
            testTmdb.Text = "测试 TMDB";
            testTmdb.Kind = ButtonKind.Soft;
            testTmdb.Size = new Size(110, 32);
            testTmdb.Location = new Point(154, 340);
            testTmdb.Click += async delegate { await Test("tmdb"); };
            Controls.Add(testTmdb);

            var cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Kind = ButtonKind.Secondary;
            cancel.Size = new Size(88, 34);
            cancel.Location = new Point(352, 398);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            var ok = new FlatButton();
            ok.Text = "保存";
            ok.Kind = ButtonKind.Primary;
            ok.Size = new Size(96, 34);
            ok.Location = new Point(448, 398);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private class TestResult
        {
            public bool Ok;
            public string Message = "";
        }

        private async Task Test(string source)
        {
            _lblTest.ForeColor = Theme.Muted;
            _lblTest.Text = "正在连接…";
            Config cfg = new Config();
            cfg.TmdbKey = _txtKey.Text.Trim();
            cfg.Proxy = _txtProxy.Text.Trim();

            TestResult res = await Task.Run(delegate
            {
                var r = new TestResult();
                string message = "";
                try
                {
                    r.Ok = MetadataClient.TestConnection(source, cfg, out message);
                }
                catch (Exception ex)
                {
                    r.Ok = false;
                    message = ex.Message;
                }
                r.Message = message;
                return r;
            });

            _lblTest.Text = res.Message;
            _lblTest.ForeColor = res.Ok ? Theme.Green : Theme.Red;
        }

        public void ApplyTo(Config config)
        {
            config.TmdbKey = _txtKey.Text.Trim();
            config.Proxy = _txtProxy.Text.Trim();
            config.Language = _cboLanguage.SelectedIndex == 1 ? "orig" : "cn";
        }
    }
}
