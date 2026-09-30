using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ZoomClipboard;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim LoginGate = new(1, 1);
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZoomClipboard");
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    private static readonly string AuthPath = Path.Combine(ConfigDirectory, "auth.dat");
    private static readonly string ClientIdPath = Path.Combine(ConfigDirectory, "client-id.txt");
    private const string RedirectUri = "http://127.0.0.1:8765/callback";

    private sealed record Config(string? ChannelId = null, string? Contact = null);
    private sealed record Auth(string ClientId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
#if ZOOM_GUI
                ApplicationConfiguration.Initialize();
                using var instanceMutex = new Mutex(false, @"Local\ZoomClipboard.UI.SingleInstance", out var firstInstance);
                if (!firstInstance)
                {
                    ActivateExistingInstance();
                    return 0;
                }
                Application.Run(new ZoomClipboardApplicationContext());
                return 0;
#else
                return Usage();
#endif
            }
#if ZOOM_GUI
            if (args.Length == 2 && args[0].Equals("--upload", StringComparison.OrdinalIgnoreCase))
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new UploadForm(args[1]));
                return 0;
            }
#endif
            return RunCommandAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCommandAsync(string[] args) => args[0].ToLowerInvariant() switch
    {
        "inspect-burp" when args.Length == 2 => InspectBurp(args[1]),
        "login" when args.Length == 1 => await Login(SavedClientId),
        "login" when args.Length == 2 => await Login(args[1]),
        "logout" => Logout(),
        "configure-channel" when args.Length == 2 => SaveConfig(new Config(ChannelId: args[1])),
        "configure-contact" when args.Length == 2 => SaveConfig(new Config(Contact: args[1])),
        "channels" => await ListChannels(),
        "list-files" => await ListFiles(),
        "upload" when args.Length == 2 => await Upload(args[1]),
        "upload-link" when args.Length == 2 => await UploadLink(args[1]),
        "download-latest" when args.Length == 2 => await DownloadLatest(args[1]),
        "install-context" => InstallContextMenu(),
        "uninstall-context" => UninstallContextMenu(),
        _ => Usage()
    };

    private static int Usage()
    {
        Console.WriteLine("""
            ZoomClipboard

              inspect-burp <burp.xml>       Show a sanitized Zoom upload summary
              login [public-client-id]      Sign in with Zoom in your browser
              logout                        Remove saved Zoom login
              channels                      List Team Chat channels
              list-files                    List files in the selected channel
              configure-channel <id>        Select a private clipboard channel
              configure-contact <id/email>  Select a Zoom contact instead
              upload <file>                 Send a file to the configured destination
              upload-link <file>            Send a file and copy its download link
              download-latest <folder>      Download the newest file to a folder
              install-context               Add Explorer right-click commands
              uninstall-context             Remove Explorer right-click commands

            Login stores OAuth tokens encrypted for your Windows user. ZOOM_CLIPBOARD_TOKEN overrides login.
            """);
        return 2;
    }

    private static async Task<string> RequireToken()
    {
        if (Environment.GetEnvironmentVariable("ZOOM_CLIPBOARD_TOKEN") is { Length: > 20 } token) return token;
        var auth = LoadAuth();
        if (auth.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return auth.AccessToken;
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = auth.RefreshToken, ["client_id"] = auth.ClientId
        });
        using var response = await client.PostAsync("https://zoom.us/oauth/token", form);
        await EnsureSuccess(response);
        var updated = ParseAuth(auth.ClientId, await response.Content.ReadAsStringAsync(), auth.RefreshToken);
        SaveAuth(updated);
        return updated.AccessToken;
    }

    private static Auth LoadAuth()
    {
        if (!File.Exists(AuthPath)) throw new InvalidOperationException("Run login <public-client-id> first.");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(AuthPath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Auth>(bytes) ?? throw new InvalidDataException("Saved login is invalid.");
    }

    private static void SaveAuth(Auth auth)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(auth), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AuthPath, bytes);
        File.WriteAllText(ClientIdPath, auth.ClientId);
    }

    private static int Logout()
    {
        if (File.Exists(AuthPath)) File.Delete(AuthPath);
        Console.WriteLine("Saved Zoom login removed. If ZOOM_CLIPBOARD_TOKEN is set, remove that override separately.");
        return 0;
    }

    internal static bool IsSignedIn => File.Exists(AuthPath) ||
        Environment.GetEnvironmentVariable("ZOOM_CLIPBOARD_TOKEN") is { Length: > 20 };
    internal static bool IsOnboardingComplete => IsSignedIn && !string.IsNullOrWhiteSpace(SavedChannelId);
    internal static bool HasTokenOverride => Environment.GetEnvironmentVariable("ZOOM_CLIPBOARD_TOKEN") is { Length: > 20 };
    internal static string SavedClientId
    {
        get
        {
            if (File.Exists(ClientIdPath)) return File.ReadAllText(ClientIdPath).Trim();
            if (File.Exists(AuthPath)) return LoadAuth().ClientId;
            return DefaultClientId.Value;
        }
    }
    internal static Task<int> SignInForUi(string clientId) => Login(clientId);
    internal static int SignOutForUi() => Logout();
    internal static Task<int> UploadLinkForUi(string path, IProgress<int>? progress = null) => UploadLink(path, progress);
    internal static Task<int> DownloadLatestForUi(string folder) => DownloadLatest(folder);
    internal sealed record UploadedFile(string FileId, string Name, long Size, DateTimeOffset UploadedAt);

    internal static async Task<IReadOnlyList<UploadedFile>> GetUploadedFilesForUi()
    {
        var config = LoadConfig();
        var selector = config.ChannelId is not null
            ? $"to_channel={Uri.EscapeDataString(config.ChannelId)}"
            : config.Contact is not null
                ? $"to_contact={Uri.EscapeDataString(config.Contact)}"
                : throw new InvalidOperationException("Choose a destination channel first.");
        using var client = await CreateClient();
        var files = new List<UploadedFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? nextPage = null;
        for (var page = 0; page < 5; page++)
        {
            var url = $"https://api.zoom.us/v2/chat/users/me/messages?{selector}&page_size=100";
            if (!string.IsNullOrEmpty(nextPage)) url += $"&next_page_token={Uri.EscapeDataString(nextPage)}";
            using var response = await client.GetAsync(url);
            await EnsureSuccess(response);
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            foreach (var message in root?["messages"]?.AsArray() ?? [])
            {
                var timestamp = message?["timestamp"]?.GetValue<long>()
                    ?? message?["message_timestamp"]?.GetValue<long>() ?? 0;
                var date = timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : DateTimeOffset.MinValue;
                foreach (var file in message?["files"]?.AsArray() ?? [])
                {
                    var id = file?["file_id"]?.GetValue<string>();
                    var name = file?["file_name"]?.GetValue<string>();
                    // Deleted attachments can remain in message history with only their ID.
                    // Keep real zero-byte files, but do not display these nameless references.
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !seen.Add(id)) continue;
                    var size = file?["file_size"]?.GetValue<long>() ?? 0;
                    files.Add(new UploadedFile(id, name, size, date));
                }
            }
            nextPage = root?["next_page_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(nextPage)) break;
        }
        return files.OrderByDescending(f => f.UploadedAt).ToArray();
    }

    internal static async Task<string> GetFileLinkForUi(string fileId)
    {
        using var client = await CreateClient();
        using var response = await client.GetAsync($"https://api.zoom.us/v2/chat/files/{Uri.EscapeDataString(fileId)}");
        await EnsureSuccess(response);
        var info = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var link = info?["download_url"]?.GetValue<string>();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Zoom did not return a valid download link.");
        return link;
    }

    internal static async Task DeleteFileForUi(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("The file ID is missing.");
        using var client = await CreateClient();
        using var response = await client.DeleteAsync($"https://api.zoom.us/v2/chat/files/{Uri.EscapeDataString(fileId)}");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                "Zoom did not allow this file to be deleted. Add the team_chat:delete:file scope, then sign out and sign in again.");
        await EnsureSuccess(response);
    }

    internal static async Task<string> DownloadFileForUi(UploadedFile file, string destinationFolder)
    {
        var destination = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(destination);
        var link = await GetFileLinkForUi(file.FileId);
        using var client = await CreateClient();
        using var response = await SendFollowingRedirects(client,
            uri => new HttpRequestMessage(HttpMethod.Get, uri), new Uri(link));
        await EnsureSuccess(response);
        var outputPath = UniquePath(destination, SanitizeFileName(file.Name));
        await using (var output = File.Create(outputPath))
            await response.Content.CopyToAsync(output);
        return outputPath;
    }
    internal static int SaveChannelForUi(string id) => SaveConfig(new Config(ChannelId: id));
    internal static void InstallContextForUi()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
        SetCommand(@"Software\Classes\*\shell\ZoomClipboardUpload", "Copy to Zoom Clipboard",
            $"\"{exe}\" --upload \"%1\"", exe);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AllFilesystemObjects\shell\ZoomClipboardUpload", false);
        InstallSendToShortcut(exe);
        var cliExe = Path.Combine(Path.GetDirectoryName(exe)!, "ZoomClipboard.exe");
        if (File.Exists(cliExe))
            SetCommand(@"Software\Classes\Directory\Background\shell\ZoomClipboardPaste", "Paste from Zoom Clipboard",
                $"\"{cliExe}\" download-latest \"%V\"");
        NotifyExplorer();
    }
    internal static bool IsContextInstalled =>
        Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\ZoomClipboardUpload\command") is not null;
    internal static string? SavedChannelId
    {
        get
        {
            if (!File.Exists(ConfigPath)) return null;
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))?.ChannelId;
        }
    }

    internal static async Task<(string Name, string Email)?> GetUserProfile()
    {
        using var client = await CreateClient();
        using var response = await client.GetAsync("https://api.zoom.us/v2/users/me");
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden) return null;
        await EnsureSuccess(response);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var name = root?["display_name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            name = string.Join(" ", new[] { root?["first_name"]?.GetValue<string>(), root?["last_name"]?.GetValue<string>() }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return (string.IsNullOrWhiteSpace(name) ? "Zoom user" : name, root?["email"]?.GetValue<string>() ?? "");
    }

    private static Auth ParseAuth(string clientId, string json, string? fallbackRefresh = null)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("Zoom returned an empty token response.");
        var access = root["access_token"]?.GetValue<string>() ?? throw new InvalidDataException("Zoom returned no access token.");
        var refresh = root["refresh_token"]?.GetValue<string>() ?? fallbackRefresh
            ?? throw new InvalidDataException("Zoom returned no refresh token.");
        var seconds = root["expires_in"]?.GetValue<int>() ?? 3600;
        return new Auth(clientId, access, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    private static async Task<int> Login(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Public Client ID is required.");
        if (!await LoginGate.WaitAsync(0))
            throw new InvalidOperationException("Zoom sign-in is already open. Complete it in your browser.");

        try
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:8765/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException(
                    "Zoom sign-in could not start because another sign-in window or application is using the local callback. " +
                    "Finish or close the other sign-in, then try again.", ex);
            }
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var url = "https://zoom.us/oauth/authorize?response_type=code" +
                $"&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";
            Console.WriteLine("Opening Zoom sign-in. Complete authorization within 5 minutes.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            while (true)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                if (context.Request.Url?.AbsolutePath != "/callback")
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }
                var query = context.Request.QueryString;
                var validState = CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(query["state"] ?? ""), Encoding.ASCII.GetBytes(state));
                if (!validState)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    throw new InvalidOperationException("OAuth state did not match; sign-in was rejected.");
                }
                var code = query["code"];
                var error = query["error"];
                var success = code is not null && error is null;
                var message = success ? "Zoom authorization received. You may close this tab." : "Zoom authorization was not completed.";
                var body = Encoding.UTF8.GetBytes($"<html><body><p>{message}</p></body></html>");
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
                listener.Stop();
                if (!success) throw new InvalidOperationException($"Zoom authorization failed: {error ?? "missing code"}");
                using var client = new HttpClient();
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = clientId,
                    ["code"] = code!, ["redirect_uri"] = RedirectUri, ["code_verifier"] = verifier
                });
                using var response = await client.PostAsync("https://zoom.us/oauth/token", form);
                await EnsureSuccess(response);
                SaveAuth(ParseAuth(clientId, await response.Content.ReadAsStringAsync()));
                Console.WriteLine("Signed in. Tokens are encrypted for this Windows user.");
                return 0;
            }
        }
        finally
        {
            LoginGate.Release();
        }
    }

    private static void ActivateExistingInstance()
    {
        var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id) continue;
                var found = false;
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out var processId);
                    if (processId != process.Id) return true;
                    ShowWindow(window, 9);
                    SetForegroundWindow(window);
                    found = true;
                    return false;
                }, IntPtr.Zero);
                if (found) return;
            }
        }

        MessageBox.Show("Zoom Clipboard is already running in the notification area.",
            "Zoom Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Config LoadConfig()
    {
        if (!File.Exists(ConfigPath))
            throw new InvalidOperationException("Run configure-channel <id> or configure-contact <id/email> first.");
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))
               ?? throw new InvalidOperationException("Configuration is invalid.");
    }

    private static int SaveConfig(Config config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        Console.WriteLine($"Destination saved in {ConfigPath}");
        return 0;
    }

    private static async Task<HttpClient> CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RequireToken());
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZoomClipboard/1.0");
        return client;
    }

    private static async Task<HttpResponseMessage> SendFollowingRedirects(
        HttpClient client, Func<Uri, HttpRequestMessage> requestFactory, Uri initial, int limit = 5)
    {
        var uri = initial;
        for (var i = 0; i <= limit; i++)
        {
            using var request = requestFactory(uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Too many redirects.");
    }

    internal static async Task<List<(string Id, string Name)>> GetChannels()
    {
        using var client = await CreateClient();
        using var response = await client.GetAsync("https://api.zoom.us/v2/chat/users/me/channels?page_size=100");
        await EnsureSuccess(response);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var channels = new List<(string Id, string Name)>();
        foreach (var channel in root["channels"]?.AsArray() ?? [])
        {
            var id = channel?["id"]?.GetValue<string>();
            var name = channel?["name"]?.GetValue<string>();
            if (id is not null) channels.Add((id, name ?? id));
        }
        return channels;
    }

    internal static async Task<(string Id, string Name)> CreateChannelForUi(string channelName)
    {
        var name = channelName.Trim();
        if (name.Length is < 1 or > 80)
            throw new ArgumentException("Enter a channel name between 1 and 80 characters.");

        using var client = await CreateClient();
        var payload = new
        {
            name,
            type = 1,
            channel_settings = new
            {
                add_member_permissions = 2,
                allow_to_add_external_users = 0,
                mention_all_permissions = 2,
                new_members_can_see_previous_messages_files = true,
                posting_permissions = 1
            }
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://api.zoom.us/v2/chat/users/me/channels", content);
        await EnsureSuccess(response);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync())
            ?? throw new InvalidDataException("Zoom returned an empty channel response.");
        var id = root["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("Zoom created the channel but did not return its ID.");
        SaveChannelForUi(id);
        return (id, root["name"]?.GetValue<string>() ?? name);
    }

    private static async Task<int> ListChannels()
    {
        foreach (var channel in await GetChannels()) Console.WriteLine($"{channel.Id}\t{channel.Name}");
        return 0;
    }

    private static async Task<int> ListFiles()
    {
        foreach (var file in await GetUploadedFilesForUi())
            Console.WriteLine($"{file.UploadedAt:yyyy-MM-dd HH:mm}\t{file.Size}\t{file.Name}");
        return 0;
    }

    private static async Task<int> Upload(string inputPath)
    {
        var result = await SendFile(inputPath);
        Console.WriteLine($"Uploaded {Path.GetFileName(inputPath)}; message id: {result.MessageId}");
        return 0;
    }

    private static async Task<int> UploadLink(string inputPath, IProgress<int>? progress = null)
    {
        var result = await SendFile(inputPath, progress);
        using var client = await CreateClient();
        var selector = result.Config.ChannelId is not null
            ? $"to_channel={Uri.EscapeDataString(result.Config.ChannelId)}"
            : $"to_contact={Uri.EscapeDataString(result.Config.Contact!)}";
        var messageUrl = $"https://api.zoom.us/v2/chat/users/me/messages/{Uri.EscapeDataString(result.MessageId)}?{selector}";
        string? fileId = null;
        for (var attempt = 0; attempt < 12 && fileId is null; attempt++)
        {
            if (attempt > 0) await Task.Delay(1000);
            using var messageResponse = await client.GetAsync(messageUrl);
            if (messageResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                var error = await messageResponse.Content.ReadAsStringAsync();
                if (error.Contains("5401", StringComparison.Ordinal) || messageResponse.StatusCode == HttpStatusCode.NotFound)
                    continue; // Zoom can take a few seconds to index an accepted upload.
            }
            await EnsureSuccess(messageResponse);
            var message = JsonNode.Parse(await messageResponse.Content.ReadAsStringAsync());
            fileId = message?["files"]?.AsArray().FirstOrDefault()?["file_id"]?.GetValue<string>()
                     ?? message?["file_ids"]?.AsArray().FirstOrDefault()?.GetValue<string>()
                     ?? message?["file_id"]?.GetValue<string>();
        }
        if (fileId is null)
            throw new InvalidOperationException($"The upload succeeded (message id: {result.MessageId}), but Zoom did not return a file ID for that message.");
        using var info = await client.GetAsync($"https://api.zoom.us/v2/chat/files/{Uri.EscapeDataString(fileId)}");
        await EnsureSuccess(info);
        var fileInfo = JsonNode.Parse(await info.Content.ReadAsStringAsync());
        var link = fileInfo?["download_url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(link))
            throw new InvalidOperationException($"The upload succeeded (message id: {result.MessageId}), but Zoom did not return a download URL.");
        if (!Uri.TryCreate(link, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Zoom returned an invalid download URL.");
        CopyToClipboard(link);
        Console.WriteLine($"Uploaded {Path.GetFileName(inputPath)}; download link copied to clipboard.");
        Console.WriteLine("Paste the link into the Citrix browser. Zoom may require sign-in, and the link may expire.");
        return 0;
    }

    private static void CopyToClipboard(string text)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { Clipboard.SetText(text); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Could not copy the link to the Windows clipboard.", error);
    }

    private static async Task<(string MessageId, Config Config)> SendFile(string inputPath, IProgress<int>? progress = null)
    {
        var path = Path.GetFullPath(inputPath);
        if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);
        var fileLength = new FileInfo(path).Length;
        if (fileLength > 20 * 1024 * 1024)
            throw new InvalidOperationException($"File is too large ({fileLength / 1024d / 1024d:0.1} MB). Zoom allows up to 20 MB per file.");
        var config = LoadConfig();
        if (config.ChannelId is null && config.Contact is null)
            throw new InvalidOperationException("No destination configured.");

        using var client = await CreateClient();
        var endpoint = new Uri("https://file.zoom.us/v2/chat/users/me/messages/files");
        HttpRequestMessage Factory(Uri uri)
        {
            var form = new MultipartFormDataContent();
            var file = new ProgressFileContent(path, progress);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var safeFileName = Path.GetFileName(path).Replace('"', '_').Replace('\r', '_').Replace('\n', '_');
            file.Headers.TryAddWithoutValidation("Content-Disposition",
                $"form-data; name=\"files\"; filename=\"{safeFileName}\"");
            form.Add(file);
            var destinationName = config.ChannelId is not null ? "to_channel" : "to_contact";
            var destinationValue = config.ChannelId ?? config.Contact!;
            var destinationPart = new StringContent(destinationValue);
            destinationPart.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"{destinationName}\"");
            form.Add(destinationPart);
            return new HttpRequestMessage(HttpMethod.Post, uri) { Content = form };
        }

        using var response = await SendFollowingRedirects(client, Factory, endpoint);
        await EnsureSuccess(response);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var messageId = result?["id"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("Zoom accepted the upload but did not return a message ID.");
        return (messageId, config);
    }

    private static async Task<int> DownloadLatest(string destinationFolder)
    {
        var destination = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(destination);
        var config = LoadConfig();
        var selector = config.ChannelId is not null
            ? $"to_channel={Uri.EscapeDataString(config.ChannelId)}"
            : $"to_contact={Uri.EscapeDataString(config.Contact!)}";

        using var client = await CreateClient();
        using var list = await client.GetAsync($"https://api.zoom.us/v2/chat/users/me/messages?{selector}&page_size=50");
        await EnsureSuccess(list);
        var root = JsonNode.Parse(await list.Content.ReadAsStringAsync())!;
        var messages = root["messages"]?.AsArray() ?? [];
        var newest = messages
            .SelectMany(m => m?["files"]?.AsArray().Select(f => new
            {
                Timestamp = m?["timestamp"]?.GetValue<long>()
                            ?? m?["message_timestamp"]?.GetValue<long>() ?? 0,
                File = f
            }) ?? [])
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefault() ?? throw new InvalidOperationException("No files were found in the configured chat.");

        var fileId = newest.File?["file_id"]?.GetValue<string>()
                     ?? throw new InvalidOperationException("The latest file has no file_id.");
        using var info = await client.GetAsync($"https://api.zoom.us/v2/chat/files/{Uri.EscapeDataString(fileId)}");
        await EnsureSuccess(info);
        var fileInfo = JsonNode.Parse(await info.Content.ReadAsStringAsync())!;
        var fileName = SanitizeFileName(fileInfo["file_name"]?.GetValue<string>() ?? fileId);
        var downloadUrl = fileInfo["download_url"]?.GetValue<string>()
                          ?? throw new InvalidOperationException("Zoom did not return a download URL.");
        var outputPath = UniquePath(destination, fileName);

        using var download = await SendFollowingRedirects(client,
            uri => new HttpRequestMessage(HttpMethod.Get, uri), new Uri(downloadUrl));
        await EnsureSuccess(download);
        await using (var output = File.Create(outputPath))
            await download.Content.CopyToAsync(output);
        Console.WriteLine($"Downloaded: {outputPath}");
        return 0;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "zoom-download" : value;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 1000) body = body[..1000];
        throw new HttpRequestException($"Zoom returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static int InspectBurp(string capturePath)
    {
        var xmlText = File.ReadAllText(capturePath)
            .Replace("<?xml version=\"1.1\"?>", "<?xml version=\"1.0\"?>")
            .Replace("\0", string.Empty);
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xmlText);
        var items = document.DocumentElement?.SelectNodes("./item")
                    ?? throw new InvalidDataException("Not a Burp XML history export.");
        var found = false;
        foreach (XmlElement item in items)
        {
            var host = item.SelectSingleNode("./host")?.InnerText ?? "";
            var path = item.SelectSingleNode("./path")?.InnerText ?? "";
            if (!host.EndsWith("zoom.us", StringComparison.OrdinalIgnoreCase) ||
                !path.StartsWith("/zoomfile/upload", StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            Console.WriteLine($"Upload captured: {item.SelectSingleNode("./method")?.InnerText} https://{host}/zoomfile/upload");
            Console.WriteLine($"Time: {item.SelectSingleNode("./time")?.InnerText}");
            Console.WriteLine("The capture uses Zoom desktop session headers and encrypted file metadata.");
            Console.WriteLine("Credentials were intentionally not imported; they are not OAuth API credentials.");
        }
        if (!found) Console.WriteLine("No Zoom desktop file-upload request was found.");
        return found ? 0 : 1;
    }

    private static int InstallContextMenu()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
        var uiExe = Path.Combine(Path.GetDirectoryName(exe)!, "ZoomClipboardUI.exe");
        var actionExe = File.Exists(uiExe) ? uiExe : exe;
        SetCommand(@"Software\Classes\*\shell\ZoomClipboardUpload", "Copy to Zoom Clipboard",
            File.Exists(uiExe) ? $"\"{uiExe}\" --upload \"%1\"" : $"\"{exe}\" upload \"%1\"", actionExe);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AllFilesystemObjects\shell\ZoomClipboardUpload", false);
        SetCommand(@"Software\Classes\Directory\Background\shell\ZoomClipboardPaste", "Paste from Zoom Clipboard",
            $"\"{exe}\" download-latest \"%V\"");
        if (File.Exists(uiExe)) InstallSendToShortcut(uiExe);
        NotifyExplorer();
        Console.WriteLine("Explorer context-menu commands installed for the current user.");
        return 0;
    }

    private static void SetCommand(string keyPath, string label, string command, string? iconPath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(null, label, RegistryValueKind.ExpandString);
        if (iconPath is not null) key.SetValue("Icon", iconPath, RegistryValueKind.ExpandString);
        using var commandKey = key.CreateSubKey("command");
        commandKey.SetValue(null, command, RegistryValueKind.ExpandString);
    }

    private static string SendToShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "SendTo", "Zoom Clipboard.lnk");

    private static void InstallSendToShortcut(string uiExe)
    {
        var path = SendToShortcutPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Build outside SendTo so an encrypted destination folder does not
        // make Explorer's launch shortcut unreadable.
        var stagingPath = Path.Combine(Path.GetTempPath(), $"ZoomClipboard-{Guid.NewGuid():N}.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Cannot create a Windows shortcut.");
        try
        {
            dynamic shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, [stagingPath])!;
            shortcut.TargetPath = uiExe;
            shortcut.Arguments = "--upload";
            shortcut.WorkingDirectory = Path.GetDirectoryName(uiExe)!;
            shortcut.Description = "Upload a file to Zoom Clipboard and copy its download link";
            shortcut.IconLocation = uiExe;
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
            File.Move(stagingPath, path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }

    private static int UninstallContextMenu()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\ZoomClipboardUpload", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AllFilesystemObjects\shell\ZoomClipboardUpload", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\ZoomClipboardPaste", false);
        if (File.Exists(SendToShortcutPath)) File.Delete(SendToShortcutPath);
        NotifyExplorer();
        Console.WriteLine("Explorer context-menu commands removed.");
        return 0;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
