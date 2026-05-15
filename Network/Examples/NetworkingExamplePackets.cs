namespace Emberglass.Network.Examples;

/// <summary>
/// Describes a client feature registration message.
/// </summary>
internal sealed class ClientFeatureRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClientFeatureRegistration"/> class.
    /// </summary>
    public ClientFeatureRegistration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientFeatureRegistration"/> class.
    /// </summary>
    /// <param name="featureName">The name of the feature being registered.</param>
    /// <param name="featureVersion">The feature version being registered.</param>
    public ClientFeatureRegistration(string featureName, string featureVersion)
    {
        FeatureName = featureName;
        FeatureVersion = featureVersion;
    }

    /// <summary>
    /// Gets or sets the name of the feature being registered.
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the feature version being registered.
    /// </summary>
    public string FeatureVersion { get; set; } = string.Empty;
}

/// <summary>
/// Describes a typed signal sent by a client.
/// </summary>
internal sealed class ClientTypedSignal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClientTypedSignal"/> class.
    /// </summary>
    public ClientTypedSignal()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientTypedSignal"/> class.
    /// </summary>
    /// <param name="message">The signal message.</param>
    public ClientTypedSignal(string message)
    {
        Message = message;
    }

    /// <summary>
    /// Gets or sets the signal message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Describes a typed signal sent by a server.
/// </summary>
internal sealed class ServerTypedSignal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTypedSignal"/> class.
    /// </summary>
    public ServerTypedSignal()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTypedSignal"/> class.
    /// </summary>
    /// <param name="message">The signal message.</param>
    public ServerTypedSignal(string message)
    {
        Message = message;
    }

    /// <summary>
    /// Gets or sets the signal message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Describes a client request to change a server setting.
/// </summary>
internal sealed class ServerSettingChangeRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSettingChangeRequest"/> class.
    /// </summary>
    public ServerSettingChangeRequest()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSettingChangeRequest"/> class.
    /// </summary>
    /// <param name="requestKey">The setting key requested by the client.</param>
    /// <param name="requestedValue">The setting value requested by the client.</param>
    public ServerSettingChangeRequest(string requestKey, bool requestedValue)
    {
        RequestKey = requestKey;
        RequestedValue = requestedValue;
    }

    /// <summary>
    /// Gets or sets the setting key requested by the client.
    /// </summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the setting value requested by the client.
    /// </summary>
    public bool RequestedValue { get; set; }
}

/// <summary>
/// Describes the server receipt for a setting change request.
/// </summary>
internal sealed class ServerSettingChangeReceipt
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSettingChangeReceipt"/> class.
    /// </summary>
    public ServerSettingChangeReceipt()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSettingChangeReceipt"/> class.
    /// </summary>
    /// <param name="requestKey">The setting key requested by the client.</param>
    /// <param name="authoritativeValue">The server-authoritative setting value.</param>
    /// <param name="isAccepted">A value indicating whether the server accepted the request.</param>
    /// <param name="rejectionReason">The reason the server rejected the request.</param>
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

    /// <summary>
    /// Gets or sets the setting key requested by the client.
    /// </summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server-authoritative setting value.
    /// </summary>
    public bool AuthoritativeValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server accepted the request.
    /// </summary>
    public bool IsAccepted { get; set; }

    /// <summary>
    /// Gets or sets the reason the server rejected the request.
    /// </summary>
    public string RejectionReason { get; set; } = string.Empty;
}

/// <summary>
/// Describes server-owned feature state.
/// </summary>
internal sealed class ServerFeatureState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerFeatureState"/> class.
    /// </summary>
    public ServerFeatureState()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerFeatureState"/> class.
    /// </summary>
    /// <param name="featureName">The name of the feature.</param>
    /// <param name="progress">The feature progress value.</param>
    /// <param name="status">The feature status text.</param>
    public ServerFeatureState(string featureName, int progress, string status)
    {
        FeatureName = featureName;
        Progress = progress;
        Status = status;
    }

    /// <summary>
    /// Gets or sets the name of the feature.
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the feature progress value.
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Gets or sets the feature status text.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
