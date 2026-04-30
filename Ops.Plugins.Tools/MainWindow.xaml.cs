using System.Windows;
using System.Windows.Controls;
using Ops.Plugins.Tools.Models;
using Ops.Plugins.Tools.Services;

namespace Ops.Plugins.Tools;

public partial class MainWindow : System.Windows.Window
{
    private string _repoRoot = string.Empty;
    private CatalogService _catalogService = null!;
    private EnvironmentCacheService _environmentCache = null!;
    private ProcessRunner _runner = null!;
    private DeploymentPreviewService _deploymentPreview = null!;
    private LastActionValuesService _lastActionValues = null!;
    private readonly ToolSettingsService _settingsService = new();
    private readonly RepositoryDiscoveryService _repositoryDiscovery = new();
    private readonly PrerequisiteService _prerequisites = new();
    private readonly Dictionary<string, FrameworkElement> _parameterControls = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CatalogAction> _actions = [];
    private CancellationTokenSource? _runCancellation;
    private bool _loadingRepositories;
    private bool _loadingAuthProfiles;

    public MainWindow()
    {
        InitializeComponent();
        var settings = _settingsService.Load();
        InitializeRepository(RepoRootLocator.FindRepoRoot(settings.LastRepositoryPath));
    }

    private void InitializeRepository(string repoRoot)
    {
        _repoRoot = repoRoot;
        _catalogService = new CatalogService(_repoRoot);
        _environmentCache = new EnvironmentCacheService(_repoRoot);
        _runner = new ProcessRunner(_repoRoot);
        _deploymentPreview = new DeploymentPreviewService(_repoRoot);
        _lastActionValues = new LastActionValuesService(_repoRoot);
        Title = $"Plugin Tools - {_repoRoot}";
        _settingsService.Save(new ToolSettings { LastRepositoryPath = _repoRoot });
        LoadRepositories();
        LoadEnvironments();
        LoadCatalog();
        CheckPrerequisites();
        _ = RefreshAuthProfilesAsync(showOutput: false);
    }

    private void LoadRepositories()
    {
        _loadingRepositories = true;
        try
        {
            var repositories = _repositoryDiscovery.Discover(_repoRoot).ToList();
            if (repositories.All(repo => !repo.Path.Equals(_repoRoot, StringComparison.OrdinalIgnoreCase)))
            {
                repositories.Insert(0, new RepositoryOption { Name = Path.GetFileName(_repoRoot), Path = _repoRoot });
            }

            RepositoryCombo.ItemsSource = repositories;
            RepositoryCombo.SelectedItem = repositories.FirstOrDefault(repo => repo.Path.Equals(_repoRoot, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _loadingRepositories = false;
        }
    }

    private void CheckPrerequisites()
    {
        var statuses = _prerequisites.Check();
        var missing = statuses.Where(s => !s.IsFound).ToList();
        PrerequisiteStatusText.Text = missing.Count == 0
            ? "Prerequisites: all required tools found."
            : $"Prerequisites: {missing.Count} missing. See Output for install guidance.";

        AppendOutput("Prerequisite check:");
        foreach (var status in statuses)
        {
            AppendOutput(status.IsFound
                ? $"  OK - {status.Name}: {status.Detail}"
                : $"  Missing - {status.Name}: {status.Detail}");
        }
    }

    private void LoadCatalog()
    {
        try
        {
            _actions = _catalogService.LoadActions();
            CategoryList.ItemsSource = new[] { "All" }.Concat(_actions.Select(a => a.Category).Distinct()).ToList();
            CategoryList.SelectedIndex = CategoryList.Items.Count > 0 ? 0 : -1;
            AppendOutput($"Loaded {_actions.Count} available action(s).");
        }
        catch (Exception ex)
        {
            AppendOutput("Catalog error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Catalog Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadEnvironments()
    {
        EnvironmentCombo.ItemsSource = _environmentCache.Load();
        if (EnvironmentCombo.Items.Count > 0)
        {
            EnvironmentCombo.SelectedIndex = 0;
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not string category)
        {
            ActionList.ItemsSource = null;
            return;
        }

        ActionList.ItemsSource = category.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? _actions.ToList()
            : _actions.Where(a => a.Category == category).ToList();
        ActionList.SelectedIndex = ActionList.Items.Count > 0 ? 0 : -1;
    }

    private void RepositoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRepositories || RepositoryCombo.SelectedItem is not RepositoryOption repository ||
            repository.Path.Equals(_repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InitializeRepository(repository.Path);
        AppendOutput($"Switched folder to {repository.Path}");
    }

    private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderSelectedAction();
    }

    private void RenderSelectedAction()
    {
        _parameterControls.Clear();
        ParameterPanel.Children.Clear();

        if (ActionList.SelectedItem is not CatalogAction action)
        {
            ActionTitle.Text = "Select an action";
            ActionDescription.Text = "Choose a category and action to see required inputs.";
            CommandPreviewText.Text = string.Empty;
            RunButton.IsEnabled = false;
            return;
        }

        ActionTitle.Text = action.Title;
        ActionDescription.Text = action.Description;
        ConfirmWritesCheck.Visibility = action.RequiresConfirmation ||
            action.RequiresConfirmationWhen.Count > 0 ||
            action.DangerLevel is "writes" or "destructive" ||
            action.Parameters.Any(p => p.RequiresConfirmation || p.ConfirmationRequired)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConfirmWritesCheck.IsChecked = false;

        foreach (var parameter in action.Parameters)
        {
            AddParameterControl(parameter);
        }

        UpdatePreview();
    }

    private void AddParameterControl(CatalogParameter parameter)
    {
        var initialValue = GetInitialParameterValue(parameter);
        var label = new TextBlock
        {
            Text = parameter.Required ? $"{parameter.Name} *" : parameter.Name,
            FontWeight = FontWeights.SemiBold,
            ToolTip = parameter.Description
        };
        ParameterPanel.Children.Add(label);

        FrameworkElement input;
        if (parameter.Type.Equals("switch", StringComparison.OrdinalIgnoreCase))
        {
            var checkBox = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(parameter.Description) ? parameter.Name : parameter.Description,
                IsChecked = string.Equals(initialValue, "true", StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 2, 0, 10)
            };
            checkBox.Checked += AnyParameter_Changed;
            checkBox.Unchecked += AnyParameter_Changed;
            input = checkBox;
        }
        else if (parameter.Type.Equals("environment", StringComparison.OrdinalIgnoreCase))
        {
            var comboBox = new ComboBox { ItemsSource = EnvironmentCombo.ItemsSource, DisplayMemberPath = "Name" };
            if (!string.IsNullOrWhiteSpace(initialValue))
            {
                comboBox.SelectedItem = EnvironmentCombo.Items
                    .OfType<EnvironmentEntry>()
                    .FirstOrDefault(entry => entry.Url.Equals(initialValue, StringComparison.OrdinalIgnoreCase));
            }

            if (comboBox.SelectedItem is null && EnvironmentCombo.SelectedItem is not null)
            {
                comboBox.SelectedItem = EnvironmentCombo.SelectedItem;
            }
            comboBox.SelectionChanged += AnyParameter_Changed;
            input = comboBox;
        }
        else if (parameter.Type.Equals("choice", StringComparison.OrdinalIgnoreCase))
        {
            var comboBox = new ComboBox { ItemsSource = parameter.Choices };
            if (!string.IsNullOrWhiteSpace(initialValue))
            {
                comboBox.SelectedItem = initialValue;
            }
            comboBox.SelectionChanged += AnyParameter_Changed;
            input = comboBox;
        }
        else
        {
            var textBox = new TextBox { Text = initialValue };
            textBox.TextChanged += AnyParameter_Changed;
            input = textBox;
        }

        _parameterControls[parameter.Name] = input;
        ParameterPanel.Children.Add(input);
    }

    private string GetInitialParameterValue(CatalogParameter parameter)
    {
        if (ActionList.SelectedItem is CatalogAction action)
        {
            var lastValues = _lastActionValues.Load(action.Id);
            if (lastValues.TryGetValue(parameter.Name, out var value))
            {
                return value;
            }
        }

        return parameter.DefaultValue ?? string.Empty;
    }

    private void AnyParameter_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void EnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentEntry entry)
        {
            EnvironmentNameText.Text = entry.Name;
            EnvironmentUrlText.Text = entry.Url;
        }
    }

    private void SaveEnvironment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _environmentCache.Save(new EnvironmentEntry
            {
                Name = EnvironmentNameText.Text.Trim(),
                Url = EnvironmentUrlText.Text.Trim()
            });
            LoadEnvironments();
            RenderSelectedAction();
            AppendOutput("Saved environment cache entry. No credentials or tokens were written.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Environment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdatePreview()
    {
        if (ActionList.SelectedItem is not CatalogAction action)
        {
            return;
        }

        var (fileName, arguments) = BuildCommand(action);
        CommandPreviewText.Text = fileName.Length == 0 ? "Complete required inputs to preview the command." : $"{fileName} {string.Join(" ", arguments.Select(QuoteForDisplay))}";

        var values = GetParameterValues();
        if (action.Id.Contains("deploy", StringComparison.OrdinalIgnoreCase) || action.Id.Contains("sync", StringComparison.OrdinalIgnoreCase))
        {
            var preview = _deploymentPreview.Create(values);
            DeploymentPreviewBox.Visibility = Visibility.Visible;
            DeploymentPreviewText.Text = $"DLL: {preview.PluginFilePath}\nStatus: {(preview.PluginFileExists ? "exists" : "not built yet")}\nAssembly: {preview.AssemblyName}\nTarget: {preview.TargetSummary}";
        }
        else
        {
            DeploymentPreviewBox.Visibility = Visibility.Collapsed;
        }

        RunButton.IsEnabled = fileName.Length > 0 && !_runner.IsRunning && (ConfirmWritesCheck.Visibility != Visibility.Visible || ConfirmWritesCheck.IsChecked == true);
    }

    private (string FileName, List<string> Arguments) BuildCommand(CatalogAction action)
    {
        var args = new List<string>();
        if (action.ActionKind.Equals("builtin", StringComparison.OrdinalIgnoreCase))
        {
            return action.Id switch
            {
                "pac-check" => PacCommandFactory.CheckVersion().ToList(),
                "pac-auth-list" => PacCommandFactory.AuthList().ToList(),
                "pac-org-who" => PacCommandFactory.OrgWho().ToList(),
                _ => (string.Empty, args)
            };
        }

        if (string.IsNullOrWhiteSpace(action.Script))
        {
            return (string.Empty, args);
        }

        args.Add("-NoProfile");
        args.Add("-ExecutionPolicy");
        args.Add("Bypass");
        args.Add("-File");
        args.Add(_catalogService.ResolveScriptPath(action.Script));

        foreach (var parameter in action.Parameters)
        {
            var value = GetParameterValue(parameter.Name);
            if (parameter.Type.Equals("switch", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("-" + parameter.Name);
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                if (parameter.Required)
                {
                    return (string.Empty, []);
                }
                continue;
            }

            args.Add("-" + parameter.Name);
            args.Add(value);
        }

        return (ProcessRunner.FindPowerShell(), args);
    }

    private string GetParameterValue(string name)
    {
        if (!_parameterControls.TryGetValue(name, out var control))
        {
            return string.Empty;
        }

        return control switch
        {
            TextBox textBox => textBox.Text.Trim(),
            CheckBox checkBox => checkBox.IsChecked == true ? "true" : "false",
            ComboBox { SelectedItem: EnvironmentEntry entry } => entry.Url,
            ComboBox { SelectedItem: string choice } => choice,
            ComboBox comboBox => comboBox.Text.Trim(),
            _ => string.Empty
        };
    }

    private Dictionary<string, string> GetParameterValues() => _parameterControls.Keys.ToDictionary(name => name, GetParameterValue, StringComparer.OrdinalIgnoreCase);

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActionList.SelectedItem is not CatalogAction action)
        {
            return;
        }

        var (fileName, arguments) = BuildCommand(action);
        if (fileName.Length == 0)
        {
            return;
        }

        _lastActionValues.Save(action.Id, GetParameterValues());
        _runCancellation = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var started = DateTimeOffset.Now;
        AppendOutput($"[{started:HH:mm:ss}] Starting {action.Title}");

        try
        {
            var result = await _runner.RunAsync(fileName, arguments, AppendOutput, _runCancellation.Token);
            AppendOutput($"[{DateTimeOffset.Now:HH:mm:ss}] Exit {result.ExitCode}; duration {result.Duration:mm\\:ss}; cancelled={result.Cancelled}");
        }
        catch (Exception ex)
        {
            AppendOutput($"[{DateTimeOffset.Now:HH:mm:ss}] Command failed to start: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Run Command", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CancelButton.IsEnabled = false;
            _runCancellation.Dispose();
            _runCancellation = null;
            UpdatePreview();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
    }

    private async void CheckPac_Click(object sender, RoutedEventArgs e) => await RunPacAsync(PacCommandFactory.CheckVersion());

    private async void CreateAuth_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnvironmentUrlText.Text))
        {
            AppendOutput("Create Auth skipped: enter an environment URL first.");
            MessageBox.Show(this, "Enter an environment URL before creating PAC auth.", "Create Auth", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = string.IsNullOrWhiteSpace(EnvironmentNameText.Text) ? "Ops Plugin Environment" : EnvironmentNameText.Text.Trim();
        var result = await RunPacAsync(PacCommandFactory.AuthCreate(EnvironmentUrlText.Text.Trim(), name));
        if (result?.ExitCode == 0)
        {
            ActiveAuthText.Text = $"Active profile: created auth for {EnvironmentUrlText.Text.Trim()}.";
            await RefreshAuthProfilesAsync(showOutput: true);
        }
    }

    private async void RefreshProfiles_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAuthProfilesAsync(showOutput: true);
    }

    private async void AuthProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAuthProfiles || AuthProfileCombo.SelectedItem is not AuthProfile profile)
        {
            return;
        }

        if (profile.IsActive)
        {
            SetActiveAuthProfile(profile);
            return;
        }

        var result = await RunPacAsync(PacCommandFactory.AuthSelect(profile.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (result?.ExitCode == 0)
        {
            AppendOutput($"PAC auth profile '{profile.DisplayName}' is now active for subsequent commands.");
            await RefreshAuthProfilesAsync(showOutput: false);
        }
    }

    private async Task RefreshAuthProfilesAsync(bool showOutput)
    {
        var (result, lines) = await RunPacCommandAsync(PacCommandFactory.AuthList(), showOutput);
        if (result?.ExitCode != 0)
        {
            AuthProfileDetailText.Text = "Auth profiles could not be loaded.";
            return;
        }

        var profiles = AuthProfileParser.Parse(lines);
        _loadingAuthProfiles = true;
        try
        {
            AuthProfileCombo.ItemsSource = profiles;
            AuthProfileCombo.SelectedItem = profiles.FirstOrDefault(profile => profile.IsActive) ?? profiles.FirstOrDefault();
        }
        finally
        {
            _loadingAuthProfiles = false;
        }

        if (AuthProfileCombo.SelectedItem is AuthProfile profile)
        {
            SetActiveAuthProfile(profile);
            await VerifyActiveOrgAsync(showOutput);
        }
        else
        {
            ActiveAuthText.Text = "Active profile: no PAC auth profiles found.";
            AuthProfileDetailText.Text = "Create an auth profile for the environment URL.";
        }
    }

    private async Task VerifyActiveOrgAsync(bool showOutput)
    {
        var (result, _) = await RunPacCommandAsync(PacCommandFactory.OrgWho(), showOutput);
        if (result?.ExitCode == 0)
        {
            ActiveAuthText.Text += " PAC env who succeeded.";
        }
    }

    private async Task<CommandResult?> RunPacAsync((string FileName, string[] Arguments) command)
    {
        var (result, _) = await RunPacCommandAsync(command, showOutput: true);
        return result;
    }

    private async Task<(CommandResult? Result, List<string> Lines)> RunPacCommandAsync((string FileName, string[] Arguments) command, bool showOutput)
    {
        _runCancellation = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var lines = new List<string>();
        try
        {
            var result = await _runner.RunAsync(command.FileName, command.Arguments, line =>
            {
                lines.Add(line);
                if (showOutput)
                {
                    AppendOutput(line);
                }
            }, _runCancellation.Token);
            AppendOutput($"PAC command exit {result.ExitCode}; duration {result.Duration:mm\\:ss}");
            return (result, lines);
        }
        catch (Exception ex)
        {
            AppendOutput("PAC command failed to start: " + ex.Message);
            MessageBox.Show(this, ex.Message, "PAC Command", MessageBoxButton.OK, MessageBoxImage.Error);
            return (null, lines);
        }
        finally
        {
            CancelButton.IsEnabled = false;
            _runCancellation.Dispose();
            _runCancellation = null;
            UpdatePreview();
        }
    }

    private void SetActiveAuthProfile(AuthProfile profile)
    {
        ActiveAuthText.Text = $"Active profile: {profile.DisplayName}";
        AuthProfileDetailText.Text = profile.Summary;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        LoadEnvironments();
        LoadCatalog();
    }

    private void AppendOutput(string line)
    {
        Dispatcher.Invoke(() =>
        {
            OutputText.AppendText(line + Environment.NewLine);
            OutputText.ScrollToEnd();
        });
    }

    private static string QuoteForDisplay(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}

internal static class CommandTupleExtensions
{
    public static (string FileName, List<string> Arguments) ToList(this (string FileName, string[] Arguments) command) => (command.FileName, command.Arguments.ToList());
}
