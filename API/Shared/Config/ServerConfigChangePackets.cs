using System;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Represents a client request to change a server-scoped config entry.
/// </summary>
/// <typeparam name="TValue">The config value type.</typeparam>
public sealed class ServerConfigChangeRequest<TValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfigChangeRequest{TValue}"/> class.
    /// </summary>
    public ServerConfigChangeRequest() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfigChangeRequest{TValue}"/> class.
    /// </summary>
    /// <param name="bindingKey">The unique binding key for the config entry.</param>
    /// <param name="value">The requested config value.</param>
    public ServerConfigChangeRequest(string bindingKey, TValue value)
    {
        BindingKey = string.IsNullOrWhiteSpace(bindingKey)
            ? throw new ArgumentException("Binding key is required.", nameof(bindingKey))
            : bindingKey;
        Value = value;
    }

    /// <summary>
    /// Gets or sets the unique binding key for the config entry.
    /// </summary>
    public string BindingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested config value.
    /// </summary>
    public TValue Value { get; set; } = default!;
}

/// <summary>
/// Represents the server response to a config change request.
/// </summary>
/// <typeparam name="TValue">The config value type.</typeparam>
public sealed class ServerConfigChangeResponse<TValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfigChangeResponse{TValue}"/> class.
    /// </summary>
    public ServerConfigChangeResponse() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfigChangeResponse{TValue}"/> class.
    /// </summary>
    /// <param name="bindingKey">The unique binding key for the config entry.</param>
    /// <param name="value">The authoritative config value.</param>
    /// <param name="isAccepted">Whether the change request was accepted.</param>
    /// <param name="rejectionReason">The reason for rejecting the request.</param>
    public ServerConfigChangeResponse(string bindingKey, TValue value, bool isAccepted, string rejectionReason)
    {
        BindingKey = string.IsNullOrWhiteSpace(bindingKey)
            ? throw new ArgumentException("Binding key is required.", nameof(bindingKey))
            : bindingKey;
        Value = value;
        IsAccepted = isAccepted;
        RejectionReason = rejectionReason ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the unique binding key for the config entry.
    /// </summary>
    public string BindingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authoritative config value.
    /// </summary>
    public TValue Value { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether the change request was accepted.
    /// </summary>
    public bool IsAccepted { get; set; }

    /// <summary>
    /// Gets or sets the reason for rejecting the request.
    /// </summary>
    public string RejectionReason { get; set; } = string.Empty;
}

/// <summary>
/// Represents the validation outcome for a server config change request.
/// </summary>
/// <typeparam name="TValue">The config value type.</typeparam>
public readonly struct ServerConfigChangeValidation<TValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfigChangeValidation{TValue}"/> struct.
    /// </summary>
    /// <param name="isAccepted">Whether the value should be accepted.</param>
    /// <param name="value">The value to apply if accepted, or the authoritative value if rejected.</param>
    /// <param name="rejectionReason">The reason for rejecting the change.</param>
    public ServerConfigChangeValidation(bool isAccepted, TValue value, string rejectionReason)
    {
        IsAccepted = isAccepted;
        Value = value;
        RejectionReason = rejectionReason ?? string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether the change request is accepted.
    /// </summary>
    public bool IsAccepted { get; }

    /// <summary>
    /// Gets the value to apply or return as authoritative.
    /// </summary>
    public TValue Value { get; }

    /// <summary>
    /// Gets the reason for rejecting the change.
    /// </summary>
    public string RejectionReason { get; }

    /// <summary>
    /// Creates an acceptance result with the provided value.
    /// </summary>
    /// <param name="value">The value to apply.</param>
    /// <returns>The acceptance result.</returns>
    public static ServerConfigChangeValidation<TValue> Accept(TValue value)
        => new(true, value, string.Empty);

    /// <summary>
    /// Creates a rejection result with the authoritative value and reason.
    /// </summary>
    /// <param name="authoritativeValue">The authoritative value to return.</param>
    /// <param name="rejectionReason">The reason for rejecting the change.</param>
    /// <returns>The rejection result.</returns>
    public static ServerConfigChangeValidation<TValue> Reject(TValue authoritativeValue, string rejectionReason)
        => new(false, authoritativeValue, rejectionReason);
}
