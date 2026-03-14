using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCydia
{
    public class Package
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Size { get; set; }
        public string FileName { get; set; }
        public string DownloadUrl { get; set; }
        public bool IsInstalled { get; set; }
        public DateTime? InstallDate { get; set; }
        public string Repository { get; set; }
        public string InstallPath { get; set; }
    }

    public class RepositoryInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public string Maintainer { get; set; }
        public string Icon { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<Package> Packages { get; set; } = new List<Package>();
    }

    public class InstallConfig
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Executable { get; set; }
        public string Arguments { get; set; }
        public string InstallType { get; set; }
        public bool CreateShortcut { get; set; }
        public string ShortcutName { get; set; }
        public bool RunAfterInstall { get; set; }
        public bool AdminRights { get; set; }
        public string MinWindows { get; set; }
        public bool AddToPath { get; set; }

        public static InstallConfig Parse(string[] lines)
        {
            var config = new InstallConfig();
            config.InstallType = "portable";
            config.CreateShortcut = true;
            config.RunAfterInstall = false;
            config.AdminRights = false;
            config.AddToPath = false;

            string currentSection = "";

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }

                if (currentSection == "Package")
                {
                    string[] parts = trimmed.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();
                        bool tempBool;

                        switch (key)
                        {
                            case "Name": config.Name = value; break;
                            case "Version": config.Version = value; break;
                            case "Executable": config.Executable = value; break;
                            case "Arguments": config.Arguments = value; break;
                            case "InstallType": config.InstallType = value; break;
                            case "CreateShortcut":
                                if (bool.TryParse(value, out tempBool))
                                    config.CreateShortcut = tempBool;
                                break;
                            case "ShortcutName": config.ShortcutName = value; break;
                            case "RunAfterInstall":
                                if (bool.TryParse(value, out tempBool))
                                    config.RunAfterInstall = tempBool;
                                break;
                            case "AddToPath":
                                if (bool.TryParse(value, out tempBool))
                                    config.AddToPath = tempBool;
                                break;
                        }
                    }
                }
                else if (currentSection == "Requirements")
                {
                    string[] parts = trimmed.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();
                        bool tempBool;

                        switch (key)
                        {
                            case "AdminRights":
                                if (bool.TryParse(value, out tempBool))
                                    config.AdminRights = tempBool;
                                break;
                            case "MinWindows": config.MinWindows = value; break;
                        }
                    }
                }
            }

            return config;
        }
    }

    public partial class Form1 : Form
    {
        private ListBox categoriesListBox;
        private ListView packagesListView;
        private ListView installedListView;
        private ListView sourcesListView;
        private TextBox searchTextBox;
        private Button installButton;
        private Button removeButton;
        private Button refreshButton;
        private Button addSourceButton;
        private TabControl mainTabControl;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar progressBar;
        private RichTextBox detailsTextBox;

        private List<Package> allPackages;
        private List<Package> installedPackages;
        private List<RepositoryInfo> repositories;
        private Package selectedPackage;
        private HttpClient httpClient;
        private string appPath;
        private string reposFile;
        private string downloadsPath;
        private string appsPath;

        public Form1()
        {
            InitializeComponent();
            InitializeData();
        }

        private void InitializeComponent()
        {
            this.Text = "PCydia";
            this.Size = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            mainTabControl = new TabControl();
            mainTabControl.Dock = DockStyle.Fill;

            TabPage packagesTab = new TabPage("Пакеты");
            CreatePackagesTab(packagesTab);
            mainTabControl.TabPages.Add(packagesTab);

            TabPage installedTab = new TabPage("Установленные");
            CreateInstalledTab(installedTab);
            mainTabControl.TabPages.Add(installedTab);

            TabPage sourcesTab = new TabPage("Источники");
            CreateSourcesTab(sourcesTab);
            mainTabControl.TabPages.Add(sourcesTab);

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Готов");
            progressBar = new ToolStripProgressBar();
            progressBar.Visible = false;
            progressBar.Style = ProgressBarStyle.Marquee;
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(progressBar);

            this.Controls.Add(mainTabControl);
            this.Controls.Add(statusStrip);
        }

        private void CreatePackagesTab(TabPage tabPage)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 200;

            Panel leftPanel = new Panel();
            leftPanel.Dock = DockStyle.Fill;

            Label categoriesLabel = new Label();
            categoriesLabel.Text = "Категории";
            categoriesLabel.Dock = DockStyle.Top;

            categoriesListBox = new ListBox();
            categoriesListBox.Dock = DockStyle.Fill;
            categoriesListBox.SelectedIndexChanged += CategoriesListBox_SelectedIndexChanged;

            leftPanel.Controls.Add(categoriesListBox);
            leftPanel.Controls.Add(categoriesLabel);

            Panel rightPanel = new Panel();
            rightPanel.Dock = DockStyle.Fill;

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 35;

            Label searchLabel = new Label();
            searchLabel.Text = "Поиск:";
            searchLabel.Location = new Point(5, 8);
            searchLabel.AutoSize = true;

            searchTextBox = new TextBox();
            searchTextBox.Location = new Point(60, 5);
            searchTextBox.Width = 200;
            searchTextBox.TextChanged += SearchTextBox_TextChanged;

            installButton = new Button();
            installButton.Text = "Установить";
            installButton.Location = new Point(270, 4);
            installButton.Width = 90;
            installButton.Click += InstallButton_Click;

            removeButton = new Button();
            removeButton.Text = "Удалить";
            removeButton.Location = new Point(365, 4);
            removeButton.Width = 90;
            removeButton.Enabled = false;
            removeButton.Click += RemoveButton_Click;

            refreshButton = new Button();
            refreshButton.Text = "Обновить";
            refreshButton.Location = new Point(460, 4);
            refreshButton.Width = 90;
            refreshButton.Click += RefreshButton_Click;

            topPanel.Controls.AddRange(new Control[] { searchLabel, searchTextBox, installButton, removeButton, refreshButton });

            packagesListView = new ListView();
            packagesListView.Dock = DockStyle.Fill;
            packagesListView.View = View.Details;
            packagesListView.FullRowSelect = true;
            packagesListView.Columns.Add("Название", 180);
            packagesListView.Columns.Add("Версия", 80);
            packagesListView.Columns.Add("Категория", 100);
            packagesListView.Columns.Add("Размер", 70);
            packagesListView.Columns.Add("Источник", 150);
            packagesListView.SelectedIndexChanged += PackagesListView_SelectedIndexChanged;
            packagesListView.DoubleClick += PackagesListView_DoubleClick;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Установить", null, InstallButton_Click);
            menu.Items.Add("Удалить", null, RemoveButton_Click);
            menu.Items.Add("Информация", null, ShowPackageDetailsMenu_Click);
            packagesListView.ContextMenuStrip = menu;

            rightPanel.Controls.Add(packagesListView);
            rightPanel.Controls.Add(topPanel);

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightPanel);
            tabPage.Controls.Add(split);
        }

        private void CreateInstalledTab(TabPage tabPage)
        {
            installedListView = new ListView();
            installedListView.Dock = DockStyle.Fill;
            installedListView.View = View.Details;
            installedListView.FullRowSelect = true;
            installedListView.Columns.Add("Название", 180);
            installedListView.Columns.Add("Версия", 100);
            installedListView.Columns.Add("Дата установки", 130);
            installedListView.Columns.Add("Размер", 70);
            installedListView.Columns.Add("Путь", 200);
            installedListView.SelectedIndexChanged += InstalledListView_SelectedIndexChanged;
            installedListView.DoubleClick += InstalledListView_DoubleClick;
            tabPage.Controls.Add(installedListView);
        }

        private void CreateSourcesTab(TabPage tabPage)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 400;

            Panel leftPanel = new Panel();
            leftPanel.Dock = DockStyle.Fill;

            Label sourcesLabel = new Label();
            sourcesLabel.Text = "Источники";
            sourcesLabel.Dock = DockStyle.Top;

            sourcesListView = new ListView();
            sourcesListView.Dock = DockStyle.Fill;
            sourcesListView.View = View.Details;
            sourcesListView.FullRowSelect = true;
            sourcesListView.Columns.Add("Название", 150);
            sourcesListView.Columns.Add("URL", 200);
            sourcesListView.Columns.Add("Пакетов", 60);
            sourcesListView.Columns.Add("Обновлен", 120);

            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 35;

            addSourceButton = new Button();
            addSourceButton.Text = "Добавить";
            addSourceButton.Location = new Point(5, 5);
            addSourceButton.Width = 80;
            addSourceButton.Click += AddSourceButton_Click;

            Button removeSourceButton = new Button();
            removeSourceButton.Text = "Удалить";
            removeSourceButton.Location = new Point(90, 5);
            removeSourceButton.Width = 80;
            removeSourceButton.Click += RemoveSourceButton_Click;

            bottomPanel.Controls.AddRange(new Control[] { addSourceButton, removeSourceButton });

            leftPanel.Controls.Add(sourcesListView);
            leftPanel.Controls.Add(sourcesLabel);
            leftPanel.Controls.Add(bottomPanel);

            detailsTextBox = new RichTextBox();
            detailsTextBox.Dock = DockStyle.Fill;
            detailsTextBox.ReadOnly = true;
            detailsTextBox.BackColor = Color.White;

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(detailsTextBox);

            sourcesListView.SelectedIndexChanged += SourcesListView_SelectedIndexChanged;

            tabPage.Controls.Add(split);
        }

        private void InitializeData()
        {
            appPath = Application.StartupPath;
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PCydia/1.0");

            allPackages = new List<Package>();
            installedPackages = new List<Package>();
            repositories = new List<RepositoryInfo>();

            downloadsPath = Path.Combine(appPath, "downloads");
            appsPath = Path.Combine(appPath, "apps");
            string reposDataPath = Path.Combine(appPath, "repos");

            Directory.CreateDirectory(downloadsPath);
            Directory.CreateDirectory(appsPath);
            Directory.CreateDirectory(reposDataPath);

            reposFile = Path.Combine(appPath, "sources.list");

            LoadRepositories();
            LoadInstalledPackages();

            Task.Run(() => RefreshAllSourcesAsync());
        }

        private void LoadRepositories()
        {
            repositories.Clear();

            if (File.Exists(reposFile))
            {
                string[] lines = File.ReadAllLines(reposFile);
                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    {
                        RepositoryInfo repo = new RepositoryInfo();
                        repo.Url = line.Trim();
                        repositories.Add(repo);
                    }
                }
            }
            else
            {
                string[] defaultRepos = {
                    "https://algorithmintensity.github.io/pcydia-official-repo/"
                };
                File.WriteAllLines(reposFile, defaultRepos);
                foreach (string repo in defaultRepos)
                {
                    RepositoryInfo repoInfo = new RepositoryInfo();
                    repoInfo.Url = repo;
                    repositories.Add(repoInfo);
                }
            }
        }

        private void LoadInstalledPackages()
        {
            installedPackages.Clear();
            string dbFile = Path.Combine(appPath, "installed.db");

            if (File.Exists(dbFile))
            {
                string[] lines = File.ReadAllLines(dbFile);
                foreach (string line in lines)
                {
                    try
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 6)
                        {
                            Package pkg = new Package();
                            pkg.Id = parts[0];
                            pkg.Name = parts[1];
                            pkg.Version = parts[2];
                            pkg.Size = parts[3];
                            pkg.InstallPath = parts[4];
                            pkg.InstallDate = DateTime.Parse(parts[5]);
                            pkg.IsInstalled = true;
                            installedPackages.Add(pkg);
                        }
                    }
                    catch { }
                }
            }
        }

        private void SaveInstalledPackages()
        {
            string dbFile = Path.Combine(appPath, "installed.db");
            List<string> lines = new List<string>();

            foreach (Package p in installedPackages)
            {
                string line = string.Format("{0}|{1}|{2}|{3}|{4}|{5:yyyy-MM-dd HH:mm:ss}",
                    p.Id, p.Name, p.Version, p.Size, p.InstallPath, p.InstallDate);
                lines.Add(line);
            }

            File.WriteAllLines(dbFile, lines);
        }

        private async Task RefreshAllSourcesAsync()
        {
            try
            {
                UpdateUIStatus("Обновление источников...", true);

                allPackages.Clear();

                foreach (RepositoryInfo repo in repositories)
                {
                    await RefreshRepositoryAsync(repo);
                }

                UpdateUICategories();
                UpdateUIPackagesListView();
                UpdateUISourcesListView();

                UpdateUIStatus("Готов", false);
            }
            catch (Exception ex)
            {
                UpdateUIStatus($"Ошибка: {ex.Message}", false);
            }
        }

        private async Task RefreshRepositoryAsync(RepositoryInfo repo)
        {
            try
            {
                UpdateUIStatus($"Загрузка {repo.Url}...", true);

                string baseUrl = repo.Url.TrimEnd('/');
                string indexHtml = await httpClient.GetStringAsync($"{baseUrl}/index.html");

                ParseRepoMetadata(indexHtml, repo);
                ParsePackagesFromHtml(indexHtml, repo);

                repo.LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки {repo.Url}: {ex.Message}");
            }
        }

        private void ParseRepoMetadata(string html, RepositoryInfo repo)
        {
            Match nameMatch = Regex.Match(html, "<meta name=\"repo-name\" content=\"([^\"]+)\"");
            if (nameMatch.Success)
                repo.Name = nameMatch.Groups[1].Value;

            Match descMatch = Regex.Match(html, "<meta name=\"repo-description\" content=\"([^\"]+)\"");
            if (descMatch.Success)
                repo.Description = descMatch.Groups[1].Value;

            Match maintMatch = Regex.Match(html, "<meta name=\"repo-maintainer\" content=\"([^\"]+)\"");
            if (maintMatch.Success)
                repo.Maintainer = maintMatch.Groups[1].Value;

            if (string.IsNullOrEmpty(repo.Name))
                repo.Name = repo.Url;
        }

        private void ParsePackagesFromHtml(string html, RepositoryInfo repo)
        {
            string baseUrl = repo.Url.TrimEnd('/');

            MatchCollection tableMatches = Regex.Matches(html, "<tr><td>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td></tr>");
            foreach (Match match in tableMatches)
            {
                try
                {
                    string name = match.Groups[1].Value;
                    string ver = match.Groups[2].Value;
                    string cat = match.Groups[3].Value;
                    string size = match.Groups[4].Value;
                    string file = match.Groups[5].Value;

                    Package pkg = new Package();
                    pkg.Id = name;
                    pkg.Name = name;
                    pkg.Version = ver;
                    pkg.Category = cat;
                    pkg.Size = size;
                    pkg.FileName = file;
                    pkg.Repository = repo.Url;

                    if (!string.IsNullOrEmpty(pkg.FileName))
                    {
                        pkg.DownloadUrl = $"{baseUrl}/{pkg.FileName}";
                        AddPackageWithInstallStatus(pkg);
                    }
                }
                catch { }
            }

            MatchCollection ulMatches = Regex.Matches(html, "<li><a href=\"([^\"]+)\">([^<]+)</a>");
            foreach (Match match in ulMatches)
            {
                string file = match.Groups[1].Value;
                string name = match.Groups[2].Value;

                if (file.EndsWith(".pcydia"))
                {
                    Package pkg = new Package();
                    pkg.Id = name;
                    pkg.Name = name;
                    pkg.Version = "1.0";
                    pkg.Category = "Разное";
                    pkg.Size = "?";
                    pkg.FileName = file;
                    pkg.DownloadUrl = $"{baseUrl}/{file}";
                    pkg.Repository = repo.Url;
                    AddPackageWithInstallStatus(pkg);
                }
            }
        }

        private void AddPackageWithInstallStatus(Package pkg)
        {
            Package installed = installedPackages.FirstOrDefault(ip => ip.Id == pkg.Id);
            if (installed != null)
            {
                pkg.IsInstalled = true;
                pkg.InstallDate = installed.InstallDate;
                pkg.InstallPath = installed.InstallPath;
            }
            allPackages.Add(pkg);
        }

        private void UpdateUIStatus(string text, bool showProgress)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateUIStatus(text, showProgress)));
                return;
            }

            statusLabel.Text = text;
            progressBar.Visible = showProgress;
        }

        private void UpdateUICategories()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUICategories));
                return;
            }

            categoriesListBox.Items.Clear();
            categoriesListBox.Items.Add("Все");

            var cats = allPackages.Select(p => p.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);
            foreach (string cat in cats)
                categoriesListBox.Items.Add(cat);

            if (categoriesListBox.Items.Count > 0)
                categoriesListBox.SelectedIndex = 0;
        }

        private void UpdateUIPackagesListView()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUIPackagesListView));
                return;
            }

            packagesListView.Items.Clear();

            string category = "Все";
            if (categoriesListBox.SelectedItem != null)
                category = categoriesListBox.SelectedItem.ToString();

            string search = searchTextBox.Text.ToLower();

            IEnumerable<Package> filtered = allPackages;

            if (category != "Все")
                filtered = filtered.Where(p => p.Category == category);

            if (!string.IsNullOrEmpty(search))
                filtered = filtered.Where(p => p.Name.ToLower().Contains(search) ||
                                              (p.Description != null && p.Description.ToLower().Contains(search)));

            foreach (Package pkg in filtered.OrderBy(p => p.Name))
            {
                ListViewItem item = new ListViewItem(pkg.Name);
                item.SubItems.Add(pkg.Version ?? "?");
                item.SubItems.Add(pkg.Category);
                item.SubItems.Add(pkg.Size ?? "?");
                item.SubItems.Add(GetRepoName(pkg.Repository));
                item.Tag = pkg;

                if (pkg.IsInstalled)
                    item.BackColor = Color.LightGreen;

                packagesListView.Items.Add(item);
            }
        }

        private void UpdateUIInstalledListView()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUIInstalledListView));
                return;
            }

            installedListView.Items.Clear();
            foreach (Package pkg in installedPackages.OrderBy(p => p.Name))
            {
                ListViewItem item = new ListViewItem(pkg.Name);
                item.SubItems.Add(pkg.Version ?? "?");
                if (pkg.InstallDate.HasValue)
                    item.SubItems.Add(pkg.InstallDate.Value.ToString("yyyy-MM-dd HH:mm"));
                else
                    item.SubItems.Add("?");
                item.SubItems.Add(pkg.Size ?? "?");
                item.SubItems.Add(pkg.InstallPath ?? "?");
                item.Tag = pkg;
                installedListView.Items.Add(item);
            }
        }

        private void UpdateUISourcesListView()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateUISourcesListView));
                return;
            }

            sourcesListView.Items.Clear();
            foreach (RepositoryInfo repo in repositories)
            {
                ListViewItem item = new ListViewItem(repo.Name ?? "Unknown");
                item.SubItems.Add(repo.Url);
                item.SubItems.Add(allPackages.Count(p => p.Repository == repo.Url).ToString());
                item.SubItems.Add(repo.LastUpdated.ToString("yyyy-MM-dd HH:mm"));
                item.Tag = repo;
                sourcesListView.Items.Add(item);
            }
        }

        private void SourcesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sourcesListView.SelectedItems.Count > 0)
            {
                RepositoryInfo repo = sourcesListView.SelectedItems[0].Tag as RepositoryInfo;
                if (repo != null)
                {
                    detailsTextBox.Clear();
                    detailsTextBox.AppendText($"Название: {repo.Name}\n");
                    detailsTextBox.AppendText($"URL: {repo.Url}\n");
                    detailsTextBox.AppendText($"Описание: {repo.Description ?? "Нет"}\n");
                    detailsTextBox.AppendText($"Мейнтейнер: {repo.Maintainer ?? "Нет"}\n");
                    detailsTextBox.AppendText($"Пакетов: {allPackages.Count(p => p.Repository == repo.Url)}\n");
                    detailsTextBox.AppendText($"Обновлен: {repo.LastUpdated}\n");
                }
            }
        }

        private string GetRepoName(string url)
        {
            RepositoryInfo repo = repositories.FirstOrDefault(r => r.Url == url);
            if (repo != null)
                return repo.Name;
            return url;
        }

        private void CategoriesListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUIPackagesListView();
        }

        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            UpdateUIPackagesListView();
        }

        private void PackagesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (packagesListView.SelectedItems.Count > 0)
            {
                selectedPackage = packagesListView.SelectedItems[0].Tag as Package;
                if (selectedPackage != null)
                {
                    installButton.Text = selectedPackage.IsInstalled ? "Переустановить" : "Установить";
                    removeButton.Enabled = selectedPackage.IsInstalled;
                }
            }
        }

        private void PackagesListView_DoubleClick(object sender, EventArgs e)
        {
            ShowPackageDetails();
        }

        private void InstalledListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (installedListView.SelectedItems.Count > 0)
                selectedPackage = installedListView.SelectedItems[0].Tag as Package;
        }

        private void InstalledListView_DoubleClick(object sender, EventArgs e)
        {
            if (selectedPackage != null && Directory.Exists(selectedPackage.InstallPath))
                System.Diagnostics.Process.Start("explorer.exe", selectedPackage.InstallPath);
        }

        private void ShowPackageDetailsMenu_Click(object sender, EventArgs e)
        {
            ShowPackageDetails();
        }

        private void ShowPackageDetails()
        {
            if (selectedPackage == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Название: {selectedPackage.Name}");
            sb.AppendLine($"Версия: {selectedPackage.Version}");
            sb.AppendLine($"Категория: {selectedPackage.Category}");
            sb.AppendLine($"Размер: {selectedPackage.Size}");
            sb.AppendLine($"Источник: {GetRepoName(selectedPackage.Repository)}");
            sb.AppendLine($"Описание: {selectedPackage.Description ?? "Нет описания"}");
            sb.AppendLine($"Установлен: {(selectedPackage.IsInstalled ? "Да" : "Нет")}");

            if (selectedPackage.IsInstalled && selectedPackage.InstallPath != null)
                sb.AppendLine($"Путь: {selectedPackage.InstallPath}");

            MessageBox.Show(sb.ToString(), "Информация о пакете");
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await RefreshAllSourcesAsync();
        }

        private async Task InstallWithConfig(string zipPath, string installDir, Package pkg)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PCydia_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir);

                string[] configFiles = Directory.GetFiles(tempDir, "*.pconf");
                InstallConfig config = null;

                if (configFiles.Length > 0)
                {
                    string[] configLines = File.ReadAllLines(configFiles[0]);
                    config = InstallConfig.Parse(configLines);
                }

                if (config != null)
                {
                    if (config.AdminRights)
                    {
                        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var principal = new System.Security.Principal.WindowsPrincipal(identity);
                        if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                        {
                            MessageBox.Show("Для установки этого пакета требуются права администратора.\n" +
                                           "Перезапустите PCydia от имени администратора.",
                                           "Требуются права администратора",
                                           MessageBoxButtons.OK, MessageBoxIcon.Warning);

                            CopyDirectory(tempDir, installDir, config.Executable);
                            return;
                        }
                    }

                    CopyDirectory(tempDir, installDir, config.Executable);

                    if (config.CreateShortcut && !string.IsNullOrEmpty(config.Executable))
                    {
                        string exePath = Path.Combine(installDir, config.Executable);
                        if (File.Exists(exePath))
                        {
                            string shortcutName = config.ShortcutName ?? pkg.Name;
                            CreateShortcut(exePath, shortcutName);
                        }
                    }

                    if (config.AddToPath)
                    {
                        AddToPath(installDir);
                    }

                    if (config.InstallType == "silent" && !string.IsNullOrEmpty(config.Executable))
                    {
                        string exePath = Path.Combine(installDir, config.Executable);
                        if (File.Exists(exePath))
                        {
                            System.Diagnostics.Process.Start(exePath, config.Arguments);
                            await Task.Delay(2000);
                        }
                    }

                    if (config.RunAfterInstall && !string.IsNullOrEmpty(config.Executable))
                    {
                        string exePath = Path.Combine(installDir, config.Executable);
                        if (File.Exists(exePath))
                        {
                            System.Diagnostics.Process.Start(exePath, config.Arguments);
                        }
                    }
                }
                else
                {
                    CopyDirectory(tempDir, installDir, null);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch { }
            }
        }

        private void CopyDirectory(string source, string target, string excludeFile)
        {
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".pconf")) continue;

                string dest = Path.Combine(target, fileName);
                File.Copy(file, dest, true);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(dir);
                string dest = Path.Combine(target, dirName);
                CopyDirectory(dir, dest, null);
            }
        }

        private void CreateShortcut(string targetPath, string shortcutName)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, shortcutName + ".lnk");

                Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"));
                dynamic shell = Activator.CreateInstance(t);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Save();
            }
            catch { }
        }

        private void AddToPath(string directory)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                if (!path.Contains(directory))
                {
                    string newPath = path + ";" + directory;
                    Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("Path", newPath);
                }
            }
            catch { }
        }

        private async void InstallButton_Click(object sender, EventArgs e)
        {
            if (selectedPackage == null) return;

            try
            {
                UpdateUIStatus($"Скачивание {selectedPackage.Name}...", true);

                string downloadUrl = selectedPackage.DownloadUrl;
                string fileName = selectedPackage.FileName;
                if (string.IsNullOrEmpty(fileName))
                    fileName = $"{selectedPackage.Id}.pcydia";
                else
                    fileName = Path.GetFileName(fileName);

                string zipPath = Path.Combine(downloadsPath, fileName);

                byte[] fileData = await httpClient.GetByteArrayAsync(downloadUrl);
                File.WriteAllBytes(zipPath, fileData);

                UpdateUIStatus($"Установка {selectedPackage.Name}...", true);

                string installDir = Path.Combine(appsPath, selectedPackage.Id);
                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, true);

                await InstallWithConfig(zipPath, installDir, selectedPackage);

                File.Delete(zipPath);

                selectedPackage.IsInstalled = true;
                selectedPackage.InstallDate = DateTime.Now;
                selectedPackage.InstallPath = installDir;

                Package existing = installedPackages.FirstOrDefault(p => p.Id == selectedPackage.Id);
                if (existing != null)
                    installedPackages.Remove(existing);

                installedPackages.Add(selectedPackage);
                SaveInstalledPackages();

                UpdateUIPackagesListView();
                UpdateUIInstalledListView();

                UpdateUIStatus($"Установлено: {selectedPackage.Name}", false);

                MessageBox.Show($"Пакет {selectedPackage.Name} успешно установлен в:\n{installDir}", "Готово");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                UpdateUIStatus("Ошибка установки", false);
            }
        }

        private async void RemoveButton_Click(object sender, EventArgs e)
        {
            if (selectedPackage == null || !selectedPackage.IsInstalled) return;

            DialogResult result = MessageBox.Show($"Удалить {selectedPackage.Name}?", "Подтверждение",
                MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                try
                {
                    UpdateUIStatus($"Удаление {selectedPackage.Name}...", true);

                    await Task.Delay(500);

                    if (Directory.Exists(selectedPackage.InstallPath))
                        Directory.Delete(selectedPackage.InstallPath, true);

                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string shortcutPath = Path.Combine(desktopPath, selectedPackage.Name + ".lnk");
                    if (File.Exists(shortcutPath))
                        File.Delete(shortcutPath);

                    selectedPackage.IsInstalled = false;
                    selectedPackage.InstallDate = null;
                    selectedPackage.InstallPath = null;

                    Package inst = installedPackages.FirstOrDefault(p => p.Id == selectedPackage.Id);
                    if (inst != null)
                        installedPackages.Remove(inst);

                    SaveInstalledPackages();

                    UpdateUIPackagesListView();
                    UpdateUIInstalledListView();

                    UpdateUIStatus($"Удалено: {selectedPackage.Name}", false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                    UpdateUIStatus("Ошибка удаления", false);
                }
            }
        }

        private void AddSourceButton_Click(object sender, EventArgs e)
        {
            string url = Microsoft.VisualBasic.Interaction.InputBox("Введите URL репозитория:", "Добавить источник");
            if (!string.IsNullOrWhiteSpace(url))
            {
                RepositoryInfo newRepo = new RepositoryInfo();
                newRepo.Url = url;
                repositories.Add(newRepo);
                File.AppendAllText(reposFile, url + Environment.NewLine);

                Task.Run(async () =>
                {
                    await RefreshRepositoryAsync(newRepo);
                    UpdateUISourcesListView();
                    UpdateUICategories();
                    UpdateUIPackagesListView();
                });

                UpdateUISourcesListView();
            }
        }

        private void RemoveSourceButton_Click(object sender, EventArgs e)
        {
            if (sourcesListView.SelectedItems.Count > 0)
            {
                RepositoryInfo repo = sourcesListView.SelectedItems[0].Tag as RepositoryInfo;
                if (repo != null)
                {
                    DialogResult result = MessageBox.Show($"Удалить {repo.Name}?", "Подтверждение",
                        MessageBoxButtons.YesNo);

                    if (result == DialogResult.Yes)
                    {
                        repositories.Remove(repo);
                        allPackages.RemoveAll(p => p.Repository == repo.Url);

                        List<string> urls = new List<string>();
                        foreach (RepositoryInfo r in repositories)
                            urls.Add(r.Url);
                        File.WriteAllLines(reposFile, urls);

                        UpdateUISourcesListView();
                        UpdateUICategories();
                        UpdateUIPackagesListView();
                    }
                }
            }
        }
    }
}