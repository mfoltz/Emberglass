# Networking Primitive Examples Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build primitive-first Emberglass networking examples and docs that replace PingPong as the primary public teaching path.

**Architecture:** Keep `VNetwork` as the only stable networking promise. Add compile-oriented example DTOs and example wiring under `Network/Examples`, then document how the primitives compose into the Bloodcraft/Eclipse-style bridge story without copying consumer-owned code.

**Tech Stack:** C# net6.0, Emberglass `VNetwork`, xUnit tests under `.codex/tests/Network`, Markdown docs under `docs/`.

---

## File Structure

- Modify: `Network/Examples/PingPong.cs`
  - Replace the narrow PingPong sample with `NetworkingPrimitiveExamples`, a compile-oriented example set covering ready gate, typed signal, registration, request/receipt, server push, and fallback decision points.
- Create: `Network/Examples/NetworkingExamplePackets.cs`
  - Holds small serializable DTOs used by the examples.
- Create: `docs/networking-examples.md`
  - Primitive-first tutorial with short snippets and the Bloodcraft/Eclipse reconstruction.
- Modify: `README.md`
  - Replace the PingPong pointer with the new examples overview link.
- Create: `.codex/tests/Network/NetworkingExamplesDocumentationTests.cs`
  - Pins the docs inventory and README link.
- Create: `.codex/tests/Network/NetworkingExamplePacketTests.cs`
  - Pins DTO construction/default behavior so the examples stay serialization-friendly.

## Task 1: Document the Primitive Ladder

**Files:**
- Create: `docs/networking-examples.md`
- Modify: `README.md`
- Test: `.codex/tests/Network/NetworkingExamplesDocumentationTests.cs`

- [ ] **Step 1: Write the failing documentation inventory test**

Create `.codex/tests/Network/NetworkingExamplesDocumentationTests.cs`:

```csharp
using Xunit;

namespace Emberglass.Tests.Network;

public sealed class NetworkingExamplesDocumentationTests
{
    [Fact]
    public void NetworkingExamplesDoc_ListsPrimitiveLadder()
    {
        string repoRoot = LocateRepoRoot();
        string docs = File.ReadAllText(Path.Combine(repoRoot, "docs", "networking-examples.md"));

        Assert.Contains("Ready Gate", docs);
        Assert.Contains("Typed Signal", docs);
        Assert.Contains("Client Registration", docs);
        Assert.Contains("Request and Receipt", docs);
        Assert.Contains("Server Push", docs);
        Assert.Contains("Soft Bridge Migration", docs);
        Assert.Contains("Bloodcraft/Eclipse", docs);
    }

    [Fact]
    public void Readme_LinksToNetworkingExamples()
    {
        string repoRoot = LocateRepoRoot();
        string readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

        Assert.Contains("docs/networking-examples.md", readme);
        Assert.DoesNotContain("see PingPong", readme, StringComparison.OrdinalIgnoreCase);
    }

    static string LocateRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Emberglass.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate Emberglass repository root.");
    }
}
```

- [ ] **Step 2: Run the documentation test and verify it fails**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore --filter "NetworkingExamplesDocumentationTests"
```

Expected: fail because `docs/networking-examples.md` does not exist and README still points at PingPong.

- [ ] **Step 3: Add the networking examples overview doc**

Create `docs/networking-examples.md`:

```markdown
# Networking Examples

Emberglass networking examples are organized around primitives. Learn each primitive on its own, then compose them into paired-mod bridges such as the Bloodcraft/Eclipse client-feature flow.

## Primitive Ladder

### Ready Gate

Clients send only after `VNetwork.OnClientReady` fires or `VNetwork.IsReady` is true. Servers can use `VNetwork.OnReady` to react when a client completes the authenticated session.

### Typed Signal

Use `RegisterServerbound<T>`, `RegisterClientbound<T>`, `SendToServer`, and `SendToClient` for small fire-and-forget messages. This is the practical replacement for chat-message signaling.

### Client Registration

After the ready gate, a client can send a registration packet with a feature name and version. The server records the feature support and can send feature-specific packets later.

### Request and Receipt

Use `SendRequest<TRequest, TResponse>` when the client proposes an action and the server owns the decision. The response should include an accepted flag, an authoritative value, and an optional rejection reason.

### Server Push

After registration, the server can send typed state to one client, selected clients, or all clients. This is the clean route for config, progress, capability, or status updates.

### Soft Bridge Migration

Existing paired mods can prefer Emberglass and keep a legacy fallback while adoption settles. Treat fallback as compatibility, not the recommended new path.

## Bloodcraft/Eclipse Reconstruction

The Bloodcraft/Eclipse bridge maps cleanly onto the primitive ladder:

1. Eclipse waits for the ready gate.
2. Eclipse sends client registration.
3. Bloodcraft records the user as supporting the client-side feature.
4. Bloodcraft sends config/progress state through server push packets.
5. Eclipse receives typed state and applies it through its existing data path.
6. Client-originated changes use request/receipt so Bloodcraft remains authoritative.
7. The older `ChatMessage` bridge remains only as a fallback when Emberglass is disabled, missing, or not ready.
```

- [ ] **Step 4: Update README to point at the examples overview**

In `README.md`, replace:

```markdown
* Typed packet networking via `VNetwork`; see PingPong under `Network/Examples` for guidance.
```

with:

```markdown
* Typed packet networking via `VNetwork`; see [Networking Examples](docs/networking-examples.md) for primitive-first guidance.
```

- [ ] **Step 5: Run the documentation test and verify it passes**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore --filter "NetworkingExamplesDocumentationTests"
```

Expected: pass with 2 tests.

- [ ] **Step 6: Commit the docs overview**

Run:

```powershell
git add README.md docs\networking-examples.md .codex\tests\Network\NetworkingExamplesDocumentationTests.cs
git commit -m "Document networking primitive examples"
```

## Task 2: Add Example DTOs

**Files:**
- Create: `Network/Examples/NetworkingExamplePackets.cs`
- Test: `.codex/tests/Network/NetworkingExamplePacketTests.cs`

- [ ] **Step 1: Write the failing DTO test**

Create `.codex/tests/Network/NetworkingExamplePacketTests.cs`:

```csharp
using Emberglass.Network.Examples;
using Xunit;

namespace Emberglass.Tests.Network;

public sealed class NetworkingExamplePacketTests
{
    [Fact]
    public void ClientFeatureRegistration_HasSerializationFriendlyDefaults()
    {
        var packet = new ClientFeatureRegistration();

        Assert.Equal(string.Empty, packet.FeatureName);
        Assert.Equal(string.Empty, packet.FeatureVersion);
    }

    [Fact]
    public void ServerSettingChangeReceipt_PreservesAuthoritativeDecision()
    {
        var receipt = new ServerSettingChangeReceipt(
            requestKey: "example.enabled",
            authoritativeValue: true,
            isAccepted: false,
            rejectionReason: "Server controls this setting.");

        Assert.Equal("example.enabled", receipt.RequestKey);
        Assert.True(receipt.AuthoritativeValue);
        Assert.False(receipt.IsAccepted);
        Assert.Equal("Server controls this setting.", receipt.RejectionReason);
    }
}
```

- [ ] **Step 2: Run the DTO test and verify it fails**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore --filter "NetworkingExamplePacketTests"
```

Expected: fail because the DTOs do not exist.

- [ ] **Step 3: Add DTOs for the primitive examples**

Create `Network/Examples/NetworkingExamplePackets.cs`:

```csharp
namespace Emberglass.Network.Examples;

/// <summary>
/// Client capability registration sent after the network ready gate.
/// </summary>
public sealed class ClientFeatureRegistration
{
    public ClientFeatureRegistration() { }

    public ClientFeatureRegistration(string featureName, string featureVersion)
    {
        FeatureName = featureName;
        FeatureVersion = featureVersion;
    }

    public string FeatureName { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;
}

/// <summary>
/// Small client-to-server signal used by the typed signal example.
/// </summary>
public sealed class ClientTypedSignal
{
    public ClientTypedSignal() { }

    public ClientTypedSignal(string message)
    {
        Message = message;
    }

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Small server-to-client signal used by the typed signal example.
/// </summary>
public sealed class ServerTypedSignal
{
    public ServerTypedSignal() { }

    public ServerTypedSignal(string message)
    {
        Message = message;
    }

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Client request for a server-owned setting change.
/// </summary>
public sealed class ServerSettingChangeRequest
{
    public ServerSettingChangeRequest() { }

    public ServerSettingChangeRequest(string requestKey, bool requestedValue)
    {
        RequestKey = requestKey;
        RequestedValue = requestedValue;
    }

    public string RequestKey { get; set; } = string.Empty;

    public bool RequestedValue { get; set; }
}

/// <summary>
/// Authoritative server receipt for a requested setting change.
/// </summary>
public sealed class ServerSettingChangeReceipt
{
    public ServerSettingChangeReceipt() { }

    public ServerSettingChangeReceipt(
        string requestKey,
        bool authoritativeValue,
        bool isAccepted,
        string rejectionReason)
    {
        RequestKey = requestKey;
        AuthoritativeValue = authoritativeValue;
        IsAccepted = isAccepted;
        RejectionReason = rejectionReason;
    }

    public string RequestKey { get; set; } = string.Empty;

    public bool AuthoritativeValue { get; set; }

    public bool IsAccepted { get; set; }

    public string RejectionReason { get; set; } = string.Empty;
}

/// <summary>
/// Server-owned state pushed to registered clients.
/// </summary>
public sealed class ServerFeatureState
{
    public ServerFeatureState() { }

    public ServerFeatureState(string featureName, int progress, string status)
    {
        FeatureName = featureName;
        Progress = progress;
        Status = status;
    }

    public string FeatureName { get; set; } = string.Empty;

    public int Progress { get; set; }

    public string Status { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run the DTO test and verify it passes**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore --filter "NetworkingExamplePacketTests"
```

Expected: pass with 2 tests.

- [ ] **Step 5: Commit the DTOs**

Run:

```powershell
git add Network\Examples\NetworkingExamplePackets.cs .codex\tests\Network\NetworkingExamplePacketTests.cs
git commit -m "Add networking example DTOs"
```

## Task 3: Replace PingPong With Primitive Examples

**Files:**
- Modify: `Network/Examples/PingPong.cs`
- Test: existing build and tests

- [ ] **Step 1: Replace PingPong with primitive example wiring**

Replace the contents of `Network/Examples/PingPong.cs` with:

```csharp
using Emberglass.API.Shared;
using ProjectM.Network;

namespace Emberglass.Network.Examples;

/// <summary>
/// Compile-oriented examples that show how to compose VNetwork primitives.
/// </summary>
internal static class NetworkingPrimitiveExamples
{
    const string FEATURE_NAME = "ExampleClientFeature";
    const string FEATURE_VERSION = "1.0.0";
    const string SETTING_KEY = "example.enabled";

    static readonly Dictionary<ulong, ClientFeatureRegistration> registeredFeatures = [];
    static bool localOptimisticValue;
    static bool localAuthoritativeValue;

    /// <summary>
    /// Registers packet handlers and readiness callbacks for the current runtime.
    /// </summary>
    public static void Initialize()
    {
        if (VWorld.IsServer)
        {
            RegisterServerHandlers();
        }

        if (VWorld.IsClient)
        {
            RegisterClientHandlers();
            VNetwork.OnClientReady += RegisterClientFeatureWhenReady;

            if (VNetwork.IsReady)
            {
                RegisterClientFeatureWhenReady();
            }
        }
    }

    static void RegisterServerHandlers()
    {
        VNetwork.RegisterServerbound<ClientTypedSignal>((sender, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] client signal from {sender.PlatformId}: {packet.Message}"));

        VNetwork.RegisterServerbound<ClientFeatureRegistration>((sender, packet) =>
        {
            registeredFeatures[sender.PlatformId] = packet;
            VWorld.Log.LogInfo($"[NetworkingExamples] registered {packet.FeatureName} {packet.FeatureVersion} for {sender.PlatformId}");
            VNetwork.SendToClient(sender, new ServerFeatureState(packet.FeatureName, progress: 1, status: "registered"));
        });

        VNetwork.RegisterRequestHandler<ServerSettingChangeRequest, ServerSettingChangeReceipt>(
            (_, request) => ValidateServerSettingChange(request));
    }

    static void RegisterClientHandlers()
    {
        VNetwork.RegisterClientbound<ServerTypedSignal>((_, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] server signal: {packet.Message}"));

        VNetwork.RegisterClientbound<ServerFeatureState>((_, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] {packet.FeatureName} state: {packet.Status} ({packet.Progress})"));
    }

    static void RegisterClientFeatureWhenReady()
    {
        VNetwork.SendToServer(new ClientFeatureRegistration(FEATURE_NAME, FEATURE_VERSION));
    }

    static ServerSettingChangeReceipt ValidateServerSettingChange(ServerSettingChangeRequest request)
    {
        bool accepted = string.Equals(request.RequestKey, SETTING_KEY, StringComparison.Ordinal);
        bool authoritativeValue = accepted && request.RequestedValue;
        string rejectionReason = accepted ? string.Empty : "Unknown setting.";

        return new ServerSettingChangeReceipt(
            request.RequestKey,
            authoritativeValue,
            accepted,
            rejectionReason);
    }

    /// <summary>
    /// Demonstrates a client-originated request with rollback to the authoritative value.
    /// </summary>
    public static void RequestServerSettingChange(bool requestedValue)
    {
        if (!VWorld.IsClient || !VNetwork.IsReady)
        {
            RestoreAuthoritativeValue();
            return;
        }

        localOptimisticValue = requestedValue;

        VNetwork.SendRequest<ServerSettingChangeRequest, ServerSettingChangeReceipt>(
            VWorld.LocalUser.GetUser(),
            new ServerSettingChangeRequest(SETTING_KEY, requestedValue),
            TimeSpan.FromSeconds(10),
            ApplyServerReceipt,
            exception =>
            {
                VWorld.Log.LogWarning($"[NetworkingExamples] setting change failed: {exception.Message}");
                RestoreAuthoritativeValue();
            });
    }

    static void ApplyServerReceipt(ServerSettingChangeReceipt receipt)
    {
        if (!receipt.IsAccepted && !string.IsNullOrWhiteSpace(receipt.RejectionReason))
        {
            VWorld.Log.LogWarning($"[NetworkingExamples] setting rejected: {receipt.RejectionReason}");
        }

        localAuthoritativeValue = receipt.AuthoritativeValue;
        localOptimisticValue = receipt.AuthoritativeValue;
    }

    static void RestoreAuthoritativeValue()
    {
        localOptimisticValue = localAuthoritativeValue;
    }

    /// <summary>
    /// Demonstrates a soft bridge decision point without owning the legacy transport.
    /// </summary>
    /// <param name="sendLegacy">Compatibility sender used when Emberglass is unavailable.</param>
    public static void SendWithLegacyFallback(Action sendLegacy)
    {
        if (sendLegacy is null)
        {
            throw new ArgumentNullException(nameof(sendLegacy));
        }

        if (!VNetwork.IsReady)
        {
            sendLegacy();
            return;
        }

        VNetwork.SendToServer(new ClientTypedSignal("sent through Emberglass"));
    }
}
```

- [ ] **Step 2: Run the build and fix compile errors only inside examples**

Run:

```powershell
dotnet build Emberglass.csproj --configuration Release --no-restore
```

Expected: pass. If it fails, restrict fixes to `Network/Examples/PingPong.cs` or `Network/Examples/NetworkingExamplePackets.cs`.

- [ ] **Step 3: Run the full network test suite**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore
```

Expected: pass.

- [ ] **Step 4: Commit the primitive example replacement**

Run:

```powershell
git add Network\Examples\PingPong.cs
git commit -m "Replace PingPong with networking primitives"
```

## Task 4: Final Verification

**Files:**
- Verify all changed files

- [ ] **Step 1: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected: no output except accepted line-ending warnings.

- [ ] **Step 2: Run metadata gate**

Run:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' .codex/scripts/version-metadata.sh
```

Expected output includes:

```text
canonical_version=0.1.1
```

- [ ] **Step 3: Run canonical install/build gate**

Run:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' .codex/install.sh
```

Expected: Release build succeeds.

- [ ] **Step 4: Run full network tests**

Run:

```powershell
dotnet test .codex\tests\Network\Emberglass.Network.Tests.csproj --configuration Release --no-restore
```

Expected: all tests pass.

- [ ] **Step 5: Review final diff**

Run:

```powershell
git status --short --branch
git diff --stat origin/main...HEAD
```

Expected: commits include only the design doc, the implementation plan, networking examples docs, example DTOs, example wiring, tests, and README link.
