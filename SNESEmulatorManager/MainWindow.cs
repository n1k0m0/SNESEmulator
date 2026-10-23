using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SNESEmulator.Contracts;
using SNESEmulator.Core;
using SNESEmulator.Management;

namespace SNESEmulatorManager;
public sealed class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? scanCancellation;
    private readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private readonly LibraryStore library;
    private readonly ComboBox instances = new()
    {
        MinWidth = 220,
        DisplayMemberPath = "ProcessId"
    };
    private readonly TextBlock banner = new()
    {
        Text = "Emulatorinstanz wählen",
        FontSize = 16,
        Margin = new(12)
    }, notice = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new(12)
    };
    private readonly ListBox recent = new()
    {
        DisplayMemberPath = "Title",
        MaxHeight = 230,
        Margin = new(18)
    };
    private readonly ComboBox stateGame = new()
    {
        DisplayMemberPath = "Title"
    };
    private readonly TextBlock dashboard = new()
    {
        FontSize = 18,
        TextWrapping = TextWrapping.Wrap,
        Margin = new(18)
    };
    private readonly DataGrid games = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single
    };
    private readonly TextBox search = new()
    {
        MinWidth = 250
    }, notes = new()
    {
        AcceptsReturn = true,
        Height = 70,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly CheckBox favorite = new()
    {
        Content = "Favorit"
    };
    private readonly ListBox states = new()
    {
        DisplayMemberPath = "Name",
        MinWidth = 350
    };
    private readonly Image preview = new()
    {
        Width = 360,
        Height = 270,
        Stretch = Stretch.Uniform
    };
    private readonly TextBlock stateInfo = new()
    {
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBox stateName = new();
    private readonly StackPanel settingRows = new();
    private readonly TextBlock settingsNotice = new() { TextWrapping = TextWrapping.Wrap, Margin = new(12), Background = Brushes.LightGoldenrodYellow, Visibility = Visibility.Collapsed };
    private int settingsReadThread;
    private readonly Dictionary<string, Func<JsonElement>> editors = new();
    private readonly Dictionary<string, FrameworkElement> settingControls = new();
    private readonly Queue<string[]> argumentQueue = new();
    private bool refreshingSelection;
    private readonly TabControl tabs = new();
    private readonly List<Button> operationButtons = [];
    private readonly List<Task> tasks = [];
    private bool operation, refreshing, closing, canClose;
    private int? requestedPid;
    private readonly ManagerViewModel model = new();
    private List<StateInfo> stateItems = [];
    private string[] initialArgs;
    public int ExitCode { get; private set; }

    public MainWindow(string[] args)
    {
        initialArgs = args;
        DataContext = model;
        Title = "SNES Emulator Manager";
        Width = 1280;
        Height = 850;
        MinWidth = 1000;
        MinHeight = 700;
        Background = new SolidColorBrush(Color.FromRgb(238, 243, 249));
        FontFamily = new("Segoe UI");
        library = new(Path.Combine(UserDataPaths.GetBaseDirectory(), "manager", "library.db"));
        var root = new DockPanel();
        Content = root;
        var top = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(21, 40, 65))
        };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        banner.Foreground = Brushes.White;
        top.Children.Add(banner);
        var choice = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        top.Children.Add(choice);
        choice.Children.Add(instances);
        choice.Children.Add(Button("Instanzen aktualisieren", () => RefreshAsync(true)));
        choice.Children.Add(Button("Emulator starten", StartEmulator));
        DockPanel.SetDock(notice, Dock.Bottom);
        root.Children.Add(notice);
        root.Children.Add(tabs);
        tabs.Margin = new(12);
        var home = new StackPanel();
        home.Children.Add(dashboard);
        var homeActions = new WrapPanel();
        home.Children.Add(homeActions);
        home.Children.Add(new TextBlock { Text = "Zuletzt gestartet", Margin = new(18, 12, 18, 0), FontSize = 16 });
        home.Children.Add(recent);
        recent.MouseDoubleClick += (_, _) =>
        {
            if (recent.SelectedItem is GameItem g)
            {
                games.SelectedItem = g;
                Run(Play);
            }
        };
        homeActions.Children.Add(Button("Fortsetzen", () => SetMode(false, false)));
        homeActions.Children.Add(Button("Pause", () => SetMode(true, false)));
        homeActions.Children.Add(Button("Turbo", () => SetMode(false, true)));
        homeActions.Children.Add(Button("Reset (gewählter Modus; SRAM bleibt)", () => Send("Reset")));
        AddTab("Dashboard", home);
        var lib = new DockPanel();
        var tools = new WrapPanel();
        DockPanel.SetDock(tools, Dock.Top);
        lib.Children.Add(tools);
        tools.Children.Add(search);
        tools.Children.Add(Button("Suchen", ReloadLibrary));
        tools.Children.Add(Button("Ordner scannen", Scan));
        tools.Children.Add(Button("Scan abbrechen", () =>
        {
            scanCancellation?.Cancel();
            return Task.CompletedTask;
        }, false));
        tools.Children.Add(Button("Datenbank wiederherstellen", RecoverDatabase));
        foreach (var(header, binding, width)in new[]
        {
            ("Spiel", "Title", 220d),
            ("Datei", "Path", 380d),
            ("Vorhanden", "Exists", 80d),
            ("Favorit", "Favorite", 65d),
            ("Zuletzt gestartet", "LastPlayed", 170d)
        }

        )
            games.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(binding), Width = width });
        lib.Children.Add(games);
        var detail = new StackPanel
        {
            Margin = new(8)
        };
        DockPanel.SetDock(detail, Dock.Bottom);
        lib.Children.Insert(1, detail);
        detail.Children.Add(favorite);
        detail.Children.Add(notes);
        var actions = new WrapPanel();
        detail.Children.Add(actions);
        actions.Children.Add(Button("Spielen", Play));
        actions.Children.Add(Button("Notizen / Favorit speichern", SaveGame));
        actions.Children.Add(Button("Fehlende Datei neu zuordnen", Relink));
        games.SelectionChanged += (_, _) =>
        {
            if (games.SelectedItem is GameItem g)
            {
                favorite.IsChecked = g.Favorite;
                notes.Text = g.Notes;
            }
        };
        AddTab("Bibliothek", lib);
        var saves = new DockPanel();
        var stateActions = new WrapPanel();
        DockPanel.SetDock(stateActions, Dock.Top);
        saves.Children.Add(stateActions);
        stateActions.Children.Add(Button("Aktualisieren", ReloadStates));
        stateActions.Children.Add(Button("Neu speichern", CreateState));
        stateActions.Children.Add(Button("Laden", LoadState));
        stateActions.Children.Add(Button("Recovery prüfen / anwenden", RecoverState));
        stateActions.Children.Add(Button("Importieren", ImportState));
        stateActions.Children.Add(Button("Exportieren", ExportState));
        stateActions.Children.Add(Button("Logisch löschen", DeleteState));
        var stateDetails = new StackPanel
        {
            Width = 390,
            Margin = new(10)
        };
        DockPanel.SetDock(stateDetails, Dock.Right);
        saves.Children.Add(stateDetails);
        stateDetails.Children.Add(preview);
        stateDetails.Children.Add(stateInfo);
        stateDetails.Children.Add(new TextBlock { Text = "Anzeigename (nur Manager-Metadaten)" });
        stateDetails.Children.Add(stateName);
        stateDetails.Children.Add(new TextBlock { Text = "Manuelle Spielzuordnung (Metadaten)" });
        stateDetails.Children.Add(stateGame);
        stateDetails.Children.Add(Button("Metadaten speichern", SaveMetadata));
        saves.Children.Add(states);
        var stateHeader = new FrameworkElementFactory(typeof(TextBlock));
        stateHeader.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name") { Converter = new GameGroupConverter(() => model.Games) });
        stateHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        states.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = stateHeader } });
        states.SelectionChanged += (_, _) => Run(PreviewState);
        AddTab("Savestates", saves);
        var settings = new DockPanel();
        var settingsTools = new WrapPanel();
        DockPanel.SetDock(settingsTools, Dock.Top);
        settings.Children.Add(settingsTools);
        settingsTools.Children.Add(Button("Aktuelle Einstellungen laden", LoadSettings));
        settingsTools.Children.Add(Button("Validieren und anwenden", ApplySettings));
        DockPanel.SetDock(settingsNotice, Dock.Top);
        settings.Children.Add(settingsNotice);
        settings.Children.Add(new ScrollViewer { Content = settingRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        AddTab("Einstellungen", settings);
        AddTab("Katalog", new TextBlock { Text = "Kein freigegebener SNES-Provider eingerichtet. Der Katalog bleibt deaktiviert. Eigene .sfc/.smc-Dateien können lokal importiert werden.", Margin = new(24), TextWrapping = TextWrapping.Wrap, FontSize = 17 });
        instances.SelectionChanged += (_, _) =>
        {
            if (refreshingSelection)
                return;
            var next = instances.SelectedItem as InstanceInfo;
            if (model.SettingsDirty && next != model.Selected && MessageBox.Show(this, "Nicht angewendeten Einstellungsentwurf verwerfen?", "Instanz wechseln", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                refreshingSelection = true;
                instances.SelectedItem = model.Selected;
                refreshingSelection = false;
                return;
            }

            model.Selected = next;
            requestedPid = null;
            model.LoadedSettings = null;
            model.SettingsDirty = false;
        };
        timer.Tick += (_, _) =>
        {
            string? probe = Argument(initialArgs, "--manager-probe");
            if (probe != null && File.Exists(Path.Combine(probe, "close")))
            {
                Close();
                return;
            }

            Track(RefreshAsync(false));
        };
        Loaded += (_, _) => Run(Initialize);
        Closing += CloseAsync;
    }

    private void AddTab(string name, UIElement content) => tabs.Items.Add(new TabItem { Header = name, Content = content, Padding = new(16, 8, 16, 8) });
    private Button Button(string text, Func<Task> action, bool disable = true)
    {
        var b = new Button
        {
            Content = text,
            Margin = new(4),
            Padding = new(9, 6, 9, 6)
        };
        b.Click += (_, _) => Run(action);
        if (disable)
            operationButtons.Add(b);
        return b;
    }

    private void Track(Task task)
    {
        tasks.RemoveAll(x => x.IsCompleted);
        tasks.Add(task);
    }

    private void Run(Func<Task> action)
    {
        if (closing)
            return;
        Track(Execute(action));
    }

    private async Task Execute(Func<Task> action)
    {
        if (operation)
        {
            notice.Text = "Ein Vorgang läuft bereits.";
            return;
        }

        operation = true;
        instances.IsEnabled = false;
        foreach (var b in operationButtons)
            b.IsEnabled = false;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            notice.Text = "Vorgang abgebrochen; bereits bestätigte Transaktionen bleiben erhalten.";
        }
        catch (Exception e)
        {
            notice.Text = e.Message;
            if (initialArgs.Contains("--manager-smoke") || initialArgs.Contains("--settings-smoke"))
            {
                Console.Error.WriteLine(e);
                ExitCode = 1;
                Close();
            }
        }
        finally
        {
            operation = false;
            instances.IsEnabled = true;
            foreach (var b in operationButtons)
                b.IsEnabled = true;
            if (argumentQueue.Count > 0 && !closing)
                Run(DrainArguments);
        }
    }

    private async Task Initialize()
    {
        await library.InitializeAsync(lifetime.Token);
        if (library.Notice == null)
            await ReloadLibrary();
        else
            notice.Text = library.Notice;
        await ProcessArguments(initialArgs);
        timer.Start();
        if (Argument(initialArgs, "--manager-probe")is { } probe)
        {
            Directory.CreateDirectory(probe);
            File.WriteAllText(Path.Combine(probe, "ready.json"), JsonSerializer.Serialize(new { Pid = Environment.ProcessId, SelectedPid = model.Selected?.ProcessId }));
        }

        if (initialArgs.Contains("--manager-smoke"))
            await Smoke();
        if(initialArgs.Contains("--settings-extensions-smoke"))await OfflineSettingsExtensionsSmoke();
        if (initialArgs.Contains("--settings-smoke"))
            await OfflineSettingsSmoke();
    }

    public void DeliverArguments(string[] args)
    {
        if (args.Length > 16 || args.Any(x => x.Length > 1024))
            throw new ArgumentException("Argument budget");
        if (argumentQueue.Count >= 8)
            throw new InvalidOperationException("Aktivierungsqueue voll; bitte erneut versuchen.");
        argumentQueue.Enqueue(args.ToArray());
        if (IsLoaded && !operation)
            Run(DrainArguments);
    }

    private async Task DrainArguments()
    {
        while (argumentQueue.TryDequeue(out var args))
            await ProcessArguments(args);
    }

    private async Task ProcessArguments(string[] args)
    {
        if (Argument(args, "--probe-hold")is { } hold && Argument(initialArgs, "--manager-probe")is { } probe && Environment.GetEnvironmentVariable("SNESEMULATOR_MANAGER_TEST_ID") != null)
        {
            File.WriteAllText(Path.Combine(probe, "hold-entered"), "ready");
            await Task.Delay(int.Parse(hold), lifetime.Token);
        }

        if (int.TryParse(Argument(args, "--connect"), out int pid))
        {
            if (model.SettingsDirty && model.Selected?.ProcessId != pid)
                throw new InvalidOperationException("Nicht angewendeter Einstellungsentwurf: Instanzwechsel zuerst bestätigen.");
            requestedPid = pid;
        }

        await RefreshAsync(true);
        string? rom = Argument(args, "--rom"), state = Argument(args, "--state");
        if (rom != null)
        {
            await EnsureInstance();
            await Send("LoadRom", new PathCommand(rom));
        }

        if (state != null)
            await LoadStatePath(state);
        if (Argument(initialArgs, "--manager-probe")is { } log && Directory.Exists(log))
            File.AppendAllText(Path.Combine(log, "activation.jsonl"), JsonSerializer.Serialize(new { Args = args, SelectedPid = model.Selected?.ProcessId }) + Environment.NewLine);
    }

    private static string? Argument(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private async Task RefreshAsync(bool discovery)
    {
        if (refreshing || closing)
            return;
        refreshing = true;
        try
        {
            if (discovery || model.Selected == null || !Discovery.IsAlive(model.Selected))
            {
                var found = await Task.Run(Discovery.List, lifetime.Token);
                var previous = model.Selected;
                refreshingSelection = true;
                instances.ItemsSource = found;
                model.Selected = requestedPid.HasValue ? found.SingleOrDefault(x => x.ProcessId == requestedPid) : found.SingleOrDefault(x => previous != null && x.ProcessId == previous.ProcessId && x.StartTicks == previous.StartTicks);
                instances.SelectedItem = model.Selected;
                refreshingSelection = false;
            }

            if (model.Selected == null)
            {
                banner.Text = requestedPid.HasValue ? $"Instanz {requestedPid} nicht verbunden" : "Emulatorinstanz ausdrücklich wählen";
                dashboard.Text = "Manager bereit. Bibliothek und Savestates sind auch ohne laufenden Emulator verfügbar.";
                return;
            }

            var response = await new IpcClient(model.Selected).SendAsync(Request.Create("Status"), lifetime.Token);
            if (!response.Success)
                throw new IOException(response.Code + ": " + response.Message);
            var s = response.Value<StatusInfo>();
            banner.Text = $"Verbunden mit PID{s.ProcessId} · {s.Network}";
            dashboard.Text = $"{s.Title}\n{s.CartridgeHash}\nFeld{s.Field} · Epoche{s.Epoch} · {(s.Paused ? "Pause" : "LÃ¤uft")} · Turbo{s.Turbo} · Menü{s.MenuOpen}\n{s.RomPath}\n{s.Fault}";
        }
        catch (Exception e)when (e is IOException or OperationCanceledException)
        {
            if (!closing)
                notice.Text = e.Message;
        }
        finally
        {
            refreshing = false;
        }
    }

    private async Task<Response> Send(string command, object? payload = null)
    {
        if (model.Selected == null)
            throw new InvalidOperationException("Zuerst eine konkrete Emulatorinstanz wählen.");
        var response = await new IpcClient(model.Selected).SendAsync(Request.Create(command, payload), lifetime.Token);
        if (!response.Success)
            throw new IOException(response.Code + ": " + response.Message);
        notice.Text = "Bestätigt: " + command;
        return response;
    }

    private async Task EnsureInstance()
    {
        if (model.Selected == null)
            await StartEmulator();
    }

    private async Task StartEmulator()
    {
        string exe;
        try
        {
            exe = EmulatorLauncher.FindExecutable(AppContext.BaseDirectory);
        }
        catch (FileNotFoundException)
        {
            var pick = new OpenFileDialog
            {
                Filter = "SNESEmulator.exe|SNESEmulator.exe"
            };
            if (pick.ShowDialog(this) != true)
                return;
            exe = pick.FileName;
        }

        var info = await EmulatorLauncher.StartAsync(exe, lifetime.Token);
        requestedPid = info.ProcessId;
        await RefreshAsync(true);
    }

    private async Task SetMode(bool pause, bool turbo)
    {
        await Send("Mode", new { Paused = pause, Turbo = turbo });
        await RefreshAsync(false);
    }

    private async Task ReloadLibrary()
    {
        string query = search.Text;
        model.Games = (await library.ListAsync(query, lifetime.Token)).ToList();
        games.ItemsSource = model.Games;
        stateGame.ItemsSource = model.Games;
        recent.ItemsSource = model.Games.Where(g => g.LastPlayed != null).GroupBy(g => g.Hash).Select(g => g.First()).Take(10).ToArray();
        if (library.Notice != null)
            notice.Text = library.Notice;
    }

    private async Task Scan()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Lokale SNES-Bibliothek"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        scanCancellation = cancel;
        try
        {
            int n = await library.ScanAsync(dialog.FolderName, cancel.Token);
            notice.Text = $"{n} gültige Dateien erfasst.";
            await ReloadLibrary();
        }
        finally
        {
            scanCancellation = null;
        }
    }

    private GameItem Game() => games.SelectedItem as GameItem ?? throw new InvalidOperationException("Ein Spiel in der Bibliothek wählen.");
    private async Task SaveGame()
    {
        var g = Game();
        await library.UpdateAsync(g.Hash, favorite.IsChecked == true, notes.Text, token: lifetime.Token);
        await ReloadLibrary();
    }

    private async Task Play()
    {
        var g = Game();
        if (!File.Exists(g.Path))
            throw new FileNotFoundException("Datei fehlt. Neu zuordnen.", g.Path);
        await EnsureInstance();
        if (model.Selected == null)
            return;
        await Send("LoadRom", new PathCommand(g.Path));
        await Send("Mode", new { Paused=false, Turbo=false });
        await library.UpdateAsync(g.Hash, g.Favorite, g.Notes, true, lifetime.Token);
        await ReloadLibrary();
        await RefreshAsync(false);
    }

    private async Task Relink()
    {
        var g = Game();
        var pick = new OpenFileDialog
        {
            Filter = "SNES ROM|*.sfc;*.smc"
        };
        if (pick.ShowDialog(this) != true)
            return;
        await library.RelinkAsync(g, pick.FileName, lifetime.Token);
        await ReloadLibrary();
    }

    private async Task RecoverDatabase()
    {
        if (library.Notice == null)
        {
            notice.Text = "Datenbank ist gültig.";
            return;
        }

        if (MessageBox.Show(this, "Beschädigte Datenbank unverändert aufbewahren und eine neue Metadatenbank anlegen? ROMs und Savestates bleiben erhalten.", "Explizite Datenbank-Recovery", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        await Task.Run(() => library.RecoverAsync(lifetime.Token));
        await ReloadLibrary();
    }

    private async Task LoadSettings()
    {
        if (model.SettingsDirty && MessageBox.Show(this, "Nicht angewendete Änderungen verwerfen?", "Einstellungen", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        var target = model.Selected;
        if (target != null)
            model.LoadedSettings = (await Send("Settings")).Value<SettingsInfo>();
        else
        {
            model.LoadedSettings = await Task.Run(() =>
            {
                settingsReadThread = Environment.CurrentManagedThreadId;
                if (Discovery.List().Count > 0)
                    throw new InvalidOperationException("Laufende Instanz wählen, bevor Einstellungen bearbeitet werden.");
                var value = EmulatorSettings.Load();
                SNESEmulator.Core.InputBindings.Ensure(value);
                return new SettingsInfo(0, JsonSerializer.SerializeToElement(value), typeof(EmulatorSettings).GetProperties().Where(p => p.CanWrite&&p.Name!="AdditionalSettings").ToDictionary(p => p.Name, p => "NEXT EMULATOR START"), value.PersistenceNotice);
            }, lifetime.Token);
        }

        ShowSettingsNotice(model.LoadedSettings.PersistenceNotice);
        model.Revision = model.LoadedSettings.Revision;
        model.SettingsInstance = target;
        settingRows.Children.Clear();
        editors.Clear();
        settingControls.Clear();
        foreach (var property in model.LoadedSettings.Settings.EnumerateObject())
        {
            if (property.Name == "PersistenceNotice" || property.Name == "GamepadBindings")
                continue;
            string effect = model.LoadedSettings.Effects.GetValueOrDefault(property.Name, "READ ONLY");
            var row = new DockPanel
            {
                Margin = new(8)
            };
            row.Children.Add(new TextBlock { Text = SettingLabels.GetValueOrDefault(property.Name, property.Name) + " · " + effect, Width = 430, VerticalAlignment = VerticalAlignment.Center });
            if (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False)
            {
                var box = new CheckBox
                {
                    IsChecked = property.Value.GetBoolean(),
                    VerticalAlignment = VerticalAlignment.Center
                };
                box.Checked += (_, _) => model.SettingsDirty = true;
                box.Unchecked += (_, _) => model.SettingsDirty = true;
                box.IsEnabled = effect!="READ ONLY"&&!effect.StartsWith("LOCKED");
                settingControls[property.Name]=box;
                row.Children.Add(box);
                editors[property.Name] = () => JsonSerializer.SerializeToElement(box.IsChecked == true);
            }
            else if (property.Name is "GamepadControls" or "GamepadActions" or "KeyboardControls")
            {
                var bindings = new StackPanel();
                var defaults=property.Name=="GamepadControls"?SNESEmulator.Core.InputBindings.SNESEmulatorPadDefaults():property.Name=="GamepadActions"?SNESEmulator.Core.InputBindings.PadActions():SNESEmulator.Core.InputBindings.KeyboardDefaults();
                var values = property.Value.ValueKind == JsonValueKind.Object ? property.Value.Deserialize<Dictionary<string, string>>()! : defaults;
                var controls = new Dictionary<string, ComboBox>();
                foreach (string name in defaults.Keys)
                {
                    var line = new StackPanel
                    {
                        Orientation = Orientation.Horizontal
                    };
                    line.Children.Add(new TextBlock { Text = name, Width = 70, VerticalAlignment = VerticalAlignment.Center });
                    var combo = new ComboBox
                    {
                        IsEditable = true,
                        Width = 260,
                        ItemsSource = property.Name=="KeyboardControls"?SNESEmulator.Core.InputBindings.KeyboardKeys:BindingChoices,
                        Text = values.GetValueOrDefault(name, "")
                    };
                    combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => model.SettingsDirty = true));
                    controls[name] = combo;
                    line.Children.Add(combo);
                    bindings.Children.Add(line);
                }

                row.Children.Add(bindings);
                editors[property.Name] = () => JsonSerializer.SerializeToElement(controls.ToDictionary(x => x.Key, x => x.Value.Text));
            }
            else if(property.Name=="ResetMode")
            {
                var combo=new ComboBox{ItemsSource=new[]{"Warm","Power"},SelectedItem=property.Value.GetString(),Width=260};combo.SelectionChanged+=(_,_)=>model.SettingsDirty=true;row.Children.Add(combo);editors[property.Name]=()=>JsonSerializer.SerializeToElement((string?)combo.SelectedItem??"Warm");
            }
            else if(property.Name=="RenderFrameLimit")
            {
                var combo=new ComboBox{ItemsSource=new[]{0,60,120},SelectedItem=property.Value.GetInt32(),Width=260};combo.SelectionChanged+=(_,_)=>model.SettingsDirty=true;row.Children.Add(combo);editors[property.Name]=()=>JsonSerializer.SerializeToElement((int?)combo.SelectedItem??60);
            }
            else if(effect=="READ ONLY")
            {
                var readonlyValue=new TextBlock{Text=property.Value.GetRawText(),TextWrapping=TextWrapping.Wrap,MaxWidth=650,IsEnabled=false};row.Children.Add(readonlyValue);settingControls[property.Name]=readonlyValue;var preserved=property.Value.Clone();editors[property.Name]=()=>preserved;
            }
            else if (property.Name is "Filter" or "Upscale" or "ControllerPort")
            {
                string[] choices = property.Name == "Filter" ? ["Scharf", "CRT", "TV"] : property.Name == "Upscale" ? ["Aus", "Scale2x", "Scale3x", "HQ2x", "HQ3x", "HQ4x"] : ["Controller1", "Controller2"];
                int offset = property.Name == "ControllerPort" ? 1 : 0;
                var combo = new ComboBox
                {
                    ItemsSource = choices,
                    SelectedIndex = property.Value.GetInt32() - offset,
                    Width = 260
                };
                combo.SelectionChanged += (_, _) => model.SettingsDirty = true;
                row.Children.Add(combo);
                editors[property.Name] = () => JsonSerializer.SerializeToElement(combo.SelectedIndex + offset);
            }
            else
            {
                var editor = new TextBox
                {
                    Text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText(),
                    MinWidth = 250,
                    MaxWidth = 650,
                    IsEnabled = !effect.StartsWith("LOCKED")
                };
                editor.TextChanged += (_, _) => model.SettingsDirty = true;
                row.Children.Add(editor);
                settingControls[property.Name] = editor;
                bool text = property.Value.ValueKind == JsonValueKind.String;
                editors[property.Name] = () => text ? JsonSerializer.SerializeToElement(editor.Text) : JsonSerializer.SerializeToElement(int.Parse(editor.Text ?? "", System.Globalization.CultureInfo.InvariantCulture));
            }

            settingRows.Children.Add(row);
        }

        model.SettingsDirty = false;
    }

    private static readonly Dictionary<string, string> SettingLabels = new()
    {
        ["AudioVolume"] = "Lautstärke0–100%",
        ["Filter"] = "Bildfilter",
        ["Upscale"] = "Bildskalierung",
        ["Fullscreen"] = "Vollbild",
        ["Gamepad"] = "Gamepad aktiv",
        ["ControllerPort"] = "Lokaler Controller",
        ["GamepadDevice"] = "Gamepadgerät (-1=erstes;0–255)",
        ["GamepadControls"] = "12 SNES-Tasten (; = alternativ, & = gleichzeitig)",
        ["GamepadActions"] = "Gamepad-Menüaktionen (; alternativ, & gleichzeitig)",
        ["KeyboardControls"] = "Tastatur/Hotkeys (; alternativ, & gleichzeitig)",
        ["RenderFrameLimit"] = "Darstellungsrate (0=unbegrenzt,60,120 Hz)",
        ["PresentationVSync"] = "Darstellung mit VSync",
        ["VideoZoom"] = "Bildrand um10% beschneiden",
        ["VideoStretch"] = "Bild auf gesamte Fensterbreite strecken",
        ["TurboMode"] = "Turbo (Host)",
        ["ResetMode"] = "Resetmodus (Warm/Power; SRAM bleibt)",
        ["MediaDirectory"] = "Medienordner",
        ["Host"] = "LAN-Server",
        ["Port"] = "LAN-Port1–65535",
        ["RelayHost"] = "Relay-Server",
        ["RelayPort"] = "Relay-Port1–65535",
        ["ConnectionId"] = "Relay-Sitzung",
        ["PlayerName"] = "Spielername",
        ["Relay"] = "Relay statt LAN",
        ["Observer"] = "Als Zuschauer verbinden"
    };
    private static readonly string[] BindingChoices = new[]
    {
        ""
    }.Concat(Enumerable.Range(0, 32).Select(i => "Button" + i)).Concat(Enumerable.Range(0, 8).SelectMany(i => new[] { "Axis" + i + "+", "Axis" + i + "-" })).Concat(Enumerable.Range(0, 4).SelectMany(i => new[] { "Hat" + i + "Up", "Hat" + i + "Down", "Hat" + i + "Left", "Hat" + i + "Right" })).ToArray();
    private async Task ApplySettings()
    {
        if (model.LoadedSettings == null)
            throw new InvalidOperationException("Zuerst aktuelle Einstellungen laden.");
        if (model.SettingsInstance != model.Selected)
            throw new InvalidOperationException("Einstellungsentwurf gehört zu einer anderen Instanz. Neu laden.");
        var values = new Dictionary<string, JsonElement>();
        foreach (var p in model.LoadedSettings.Settings.EnumerateObject())
        {
            if (!editors.TryGetValue(p.Name, out var edit))
            {
                values[p.Name] = p.Value.Clone();
                continue;
            }

            values[p.Name] = edit();
        }

        var json = JsonSerializer.SerializeToElement(values);
        var candidate = json.Deserialize<EmulatorSettings>(Framing.Options) ?? throw new InvalidDataException();
        string before = JsonSerializer.Serialize(candidate);
        candidate.Validate();
        if (before != JsonSerializer.Serialize(candidate))
            throw new InvalidDataException("Ungültige Werte oder Bindings; nichts angewendet.");
        if (model.Selected == null)
        {
            if (await Task.Run(() => Discovery.List().Count > 0, lifetime.Token))
                throw new InvalidOperationException("Instanz wurde gestartet. Verbinden und Einstellungen erneut laden.");
            await Task.Run(() => SNESEmulator.Runtime.SettingsPersistence.WriteOrdered(candidate.Serialize()), lifetime.Token);
            notice.Text = "Gespeichert für nächsten Emulatorstart.";
        }
        else
        {
            var ack = (await Send("ApplySettings", new ApplySettings(model.Revision, json))).Value<SettingsInfo>();
            model.Revision = ack.Revision;
            model.LoadedSettings = ack;
            notice.Text = "Vom Emulator bestätigt und angewendet. NEXT CONNECTION gilt erst bei neuer Verbindung.";
        }

        model.SettingsDirty = false;
    }

    private void ShowSettingsNotice(string? value)
    {
        settingsNotice.Text = value ?? "";
        settingsNotice.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task OfflineSettingsSmoke()
    {
        string output = Argument(initialArgs, "--settings-smoke") ?? throw new ArgumentException("Settings smoke output");
        Directory.CreateDirectory(output);
        await LoadSettings();
        string warning = settingsNotice.Text;
        if (settingsNotice.Visibility != Visibility.Visible || settingsReadThread == Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Settings notice or background read missing");
        await RefreshAsync(false);
        if (settingsNotice.Text != warning || settingsNotice.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Refresh erased settings recovery notice");
        tabs.SelectedIndex = 3;
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "settings.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Warning = warning, Visible = settingsNotice.IsVisible, Volume = ((TextBox)settingControls["AudioVolume"]).Text, ReadThread = settingsReadThread, UiThread = Environment.CurrentManagedThreadId, SyntheticAppHooks = true }));
        Close();
    }

    private async Task OfflineSettingsExtensionsSmoke()
    {
        string output=Argument(initialArgs,"--settings-extensions-smoke")??throw new ArgumentException("Extensions probe output");
        Directory.CreateDirectory(output);await LoadSettings();
        string[] names=["FutureEnabled","FutureText","FutureObject"];
        var before=names.ToDictionary(name=>name,name=>JsonSerializer.Serialize(model.LoadedSettings!.Settings.GetProperty(name)));
        var disabled=names.ToDictionary(name=>name,name=>settingControls.TryGetValue(name,out var control)&&!control.IsEnabled);
        if(disabled.Values.Any(value=>!value))throw new InvalidOperationException("Unknown settings control was editable");
        ((TextBox)settingControls["AudioVolume"]).Text="55";await ApplySettings();
        using var saved=JsonDocument.Parse(File.ReadAllBytes(UserDataPaths.GetSettingsPath()));
        var after=names.ToDictionary(name=>name,name=>JsonSerializer.Serialize(saved.RootElement.GetProperty(name)));
        if(before.Any(entry=>after[entry.Key]!=entry.Value)||saved.RootElement.GetProperty("AudioVolume").GetInt32()!=55)
            throw new InvalidOperationException("Apply changed unknown JSON values");
        tabs.SelectedIndex=3;UpdateLayout();if(settingRows.Parent is ScrollViewer scroll)scroll.ScrollToEnd();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"settings-extensions.png")))encoder.Save(file);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Success=true,Disabled=disabled,Before=before,After=after,Volume=55,SyntheticAppHooks=true,Meaning="Unknown bool/string/object values retain identical canonical JSON after actual offline Apply; entire settings file changes deliberately for volume."}));
        Close();
    }

    private async Task ReloadStates()
    {
        string[] paths = await Task.Run(() => StateCatalog.List(UserDataPaths.GetSaveDirectory()), lifetime.Token);
        Dictionary<string, (string Name, string Hash)> metadata;
        try
        {
            metadata = await library.StateMetadataAsync(lifetime.Token);
        }
        catch (InvalidDataException)
        {
            metadata = new(StringComparer.OrdinalIgnoreCase);
            notice.Text = library.Notice ?? "Metadata unavailable; states are retained.";
        }

        stateItems = paths.Select(p => new StateInfo(p, metadata.TryGetValue(p, out var m) && m.Name.Length > 0 ? m.Name : Path.GetFileNameWithoutExtension(p), metadata.TryGetValue(p, out var h) ? h.Hash : Path.GetFileName(Path.GetDirectoryName(p)) ?? "", 9, 0, 0, null, null, File.Exists(p + ".bak"))).ToList();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(stateItems);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Hash"));
        states.ItemsSource = view;
    }

    private StateInfo State() => states.SelectedItem as StateInfo ?? throw new InvalidOperationException("Savestate wählen.");
    private async Task<Cartridge> MatchingCart(StateInfo state)
    {
        string? hash;
        try
        {
            hash = await Task.Run(() => StateCatalog.ReadIdentity(state.Path), lifetime.Token);
        }
        catch (InvalidDataException)
        {
            hash = state.Hash;
        }

        var g = model.Games.FirstOrDefault(g => g.Hash == hash && File.Exists(g.Path)) ?? (games.SelectedItem as GameItem);
        if (g == null)
            throw new InvalidOperationException("Passendes Spiel in Bibliothek wählen.");
        return await Task.Run(() => new Cartridge(AtomicFile.ReadBytes(g.Path, 8 * 1024 * 1024 + 512), g.Path), lifetime.Token);
    }

    private async Task PreviewState()
    {
        if (states.SelectedItem is not StateInfo state)
            return;
        preview.Source = null;
        stateInfo.Text = "Loading State9 preview...";
        Cartridge cart;
        try
        {
            cart = await MatchingCart(state);
        }
        catch (Exception e)when (e is IOException or InvalidDataException or InvalidOperationException)
        {
            stateInfo.Text = e.Message;
            return;
        }

        var data = await Task.Run(() => StateCatalog.Preview(state.Path, cart), lifetime.Token);
        if (!ReferenceEquals(states.SelectedItem, state))
            return;
        stateName.Text = state.Name;
        stateGame.SelectedItem = model.Games.FirstOrDefault(g => g.Hash == state.Hash);
        stateInfo.Text = data.Error ?? $"Format9 · {data.Width}×{data.Height}\n{data.Hash}\n{data.Path}";
        preview.Source = null;
        if (data.Pixels != null)
        {
            var bitmap = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null, data.Pixels, data.Width * 4);
            bitmap.Freeze();
            preview.Source = bitmap;
        }

        if (data.Error != null && data.RecoveryAvailable)
            stateInfo.Text += "\nValidierte Backup-Recovery kann ausdrücklich geprüft werden.";
    }

    private async Task CreateState()
    {
        await EnsureInstance();
        var s = (await Send("Status")).Value<StatusInfo>();
        if (s.CartridgeHash.Length == 0)
            throw new InvalidOperationException("Cartridge laden.");
        string folder = Path.Combine(UserDataPaths.GetSaveDirectory(), s.CartridgeHash);
        string path = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".snessav");
        await Send("SaveState", new PathCommand(path, s.Epoch));
        await ReloadStates();
    }

    private async Task LoadState() => await LoadStatePath(State().Path);
    private async Task LoadStatePath(string path)
    {
        string hash = await Task.Run(() => StateCatalog.ReadIdentity(path), lifetime.Token);
        await EnsureInstance();
        if (model.Selected == null)
            return;
        var status = (await Send("Status")).Value<StatusInfo>();
        if (status.CartridgeHash != hash)
        {
            var matching = model.Games.FirstOrDefault(g => g.Hash == hash && File.Exists(g.Path)) ?? throw new InvalidOperationException("Passendes ROM fehlt in der Bibliothek. Datei scannen oder neu zuordnen.");
            // Validate a complete owned state before changing the selected cartridge.
            byte[] prepared=await Task.Run(()=>{
                var cartridge=new Cartridge(AtomicFile.ReadBytes(matching.Path,8*1024*1024+512),matching.Path);
                byte[] bytes=AtomicFile.ReadBytes(path,SNESEmulator.SaveStates.StateSerializer.MaximumCompressedBytes);
                using var input=new MemoryStream(bytes,false);
                _=SNESEmulator.SaveStates.StateSerializer.Load(cartridge,input);
                return bytes;
            },lifetime.Token);
            string staging=Path.Combine(UserDataPaths.GetBaseDirectory(),"manager","pending",Guid.NewGuid().ToString("N")+".state");
            try{
                await Task.Run(()=>Directory.CreateDirectory(Path.GetDirectoryName(staging)!),lifetime.Token);
                await File.WriteAllBytesAsync(staging,prepared,lifetime.Token);
                await Send("LoadRom", new PathCommand(matching.Path, status.Epoch));
                status = (await Send("Status")).Value<StatusInfo>();
                await Send("LoadState",new PathCommand(staging,status.Epoch));
                return;
            }finally{await Task.Run(()=>{if(File.Exists(staging))File.Delete(staging);});}
        }

        await Send("LoadState", new PathCommand(path, status.Epoch));
    }

    private Task RecoverState() => RecoverStateCore(false);
    private async Task RecoverStateCore(bool confirmed)
    {
        var state = State();
        var cart = await MatchingCart(state);
        var recovered = await Task.Run(() => StateCatalog.Preview(state.Path, cart, true), lifetime.Token);
        if (recovered.Error != null)
            throw new InvalidDataException(recovered.Error);
        if (!confirmed && MessageBox.Show(this, "Validiertes Backup als neue Primärdatei wiederherstellen? Beschädigte Datei wird aufbewahrt.", "State-Recovery", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        await Task.Run(() => StateCatalog.Recover(state.Path, cart), lifetime.Token);
        await PreviewState();
    }

    private async Task ImportState()
    {
        var pick = new OpenFileDialog
        {
            Filter = "SNES State9|*.snessav"
        };
        if (pick.ShowDialog(this) != true)
            return;
        var g = Game();
        var cart = await Task.Run(() => new Cartridge(AtomicFile.ReadBytes(g.Path, 8 * 1024 * 1024 + 512)), lifetime.Token);
        string destination = Path.Combine(UserDataPaths.GetSaveDirectory(), cart.Hash, Path.GetFileName(pick.FileName));
        await Task.Run(() => StateCatalog.Import(pick.FileName, destination, cart), lifetime.Token);
        await ReloadStates();
    }

    private async Task ExportState()
    {
        var state = State();
        var cart = await MatchingCart(state);
        var pick = new SaveFileDialog
        {
            Filter = "SNES State9|*.snessav",
            FileName = Path.GetFileName(state.Path)
        };
        if (pick.ShowDialog(this) != true)
            return;
        await Task.Run(() => StateCatalog.Import(state.Path, pick.FileName, cart), lifetime.Token);
        notice.Text = "Validierte unveränderte Statebytes exportiert.";
    }

    private async Task DeleteState()
    {
        var state = State();
        if (MessageBox.Show(this, "Primärdatei und Backup logisch löschen? Aufbewahrte beschädigte Generationen bleiben erhalten.", "Savestate löschen", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        await Task.Run(() => AtomicFile.DeleteLogical(state.Path), lifetime.Token);
        await ReloadStates();
    }

    private async Task SaveMetadata()
    {
        var state = State();
        string hash = (stateGame.SelectedItem as GameItem)?.Hash ?? state.Hash;
        if (hash.Length != 64)
            hash = await Task.Run(() => StateCatalog.ReadIdentity(state.Path), lifetime.Token);
        await library.SetStateMetadataAsync(state.Path, stateName.Text, hash, lifetime.Token);
        notice.Text = "Metadata saved; state bytes unchanged.";
        await ReloadStates();
    }

    private async void CloseAsync(object? sender, CancelEventArgs e)
    {
        if (canClose)
            return;
        e.Cancel = true;
        if (closing)
            return;
        closing = true;
        timer.Stop();
        lifetime.Cancel();
        scanCancellation?.Cancel();
        try
        {
            await Task.WhenAll(tasks.ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }

        library.Dispose();
        lifetime.Dispose();
        canClose = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    private async Task Smoke()
    {
        string output = Argument(initialArgs, "--manager-smoke") ?? throw new ArgumentException("Smoke output");
        Directory.CreateDirectory(output);
        await ReloadLibrary();
        await ReloadStates();
        if (model.Games.Count == 0 || stateItems.Count == 0)
            throw new InvalidOperationException("Native smoke requires populated library and State9 fixtures");
        games.SelectedItem = model.Games[0];
        states.SelectedItem = stateItems[0];
        await PreviewState();
        if (preview.Source == null)
            throw new InvalidOperationException("Actual model.Selected State9 image required");
        string originalState = State().Path;
        string originalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(originalState)));
        stateName.Text = "Manager native renamed state";
        await SaveMetadata();
        states.SelectedItem = stateItems[0];
        await PreviewState();
        if (stateName.Text != "Manager native renamed state")
            throw new InvalidOperationException("Persisted metadata reload failed");
        if (originalHash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(originalState))))
            throw new InvalidOperationException("Metadata changed state bytes");
        await Task.Run(() => File.WriteAllText(originalState, "deliberate corrupt primary"));
        await PreviewState();
        if (preview.Source != null || !stateInfo.Text.Contains("Recovery"))
            throw new InvalidOperationException("Damaged State9 recovery must be visible");
        await RecoverStateCore(true);
        await PreviewState();
        if (preview.Source == null || originalHash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(originalState))))
            throw new InvalidOperationException("Validated UI recovery differs from original state");
        await LoadSettings();
        if (settingControls["AudioVolume"] is not TextBox volume)
            throw new InvalidOperationException("Typed volume editor missing");
        volume.Text = "55";
        await ApplySettings();
        var acknowledged = (await Send("Settings")).Value<SettingsInfo>();
        if (acknowledged.Settings.GetProperty("AudioVolume").GetInt32() != 55)
            throw new InvalidOperationException("Typed settings apply not acknowledged");
        foreach (var button in operationButtons)
            button.IsEnabled = true;
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            UpdateLayout();
            await Dispatcher.InvokeAsync(() =>
            {
            }, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"tab-{i}.png"));
            encoder.Save(file);
        }

        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { WindowLoaded = IsLoaded, Width = ActualWidth, Height = ActualHeight, Tabs = tabs.Items.Count, SelectedPid = model.Selected?.ProcessId, LibraryFiles = model.Games.Count, States = stateItems.Count, Preview = preview.Source != null, MetadataName = stateName.Text, AcknowledgedVolume = 55, SyntheticAppHooks = true, OsFocusVerified = false }));
        Close();
    }
}
