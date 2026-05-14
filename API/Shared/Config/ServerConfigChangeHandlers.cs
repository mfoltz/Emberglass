using Emberglass.API.Shared;
using ProjectM.Network;
using System;
using System.Collections.Generic;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Registers server-side handlers for config change requests.
/// </summary>
public static class ServerConfigChangeHandlers
{
    const string MissingBindingReason = "Server config binding was not registered.";
    const string ConfigUnavailableReason = "Server config entry is unavailable.";

    static readonly Dictionary<BindingRegistrationKey, object> _bindings = new();
    static readonly HashSet<Type> _registeredTypes = new();
    static readonly object _lock = new();

    /// <summary>
    /// Registers a server-scoped binding so clients can request changes.
    /// </summary>
    /// <typeparam name="TSettings">The settings type that owns the binding.</typeparam>
    /// <typeparam name="TValue">The config value type.</typeparam>
    /// <param name="binding">The binding to register.</param>
    /// <param name="validate">Optional validation callback for incoming changes.</param>
    public static void RegisterBinding<TSettings, TValue>(
        Binding<TSettings, TValue> binding,
        Func<User, TValue, ServerConfigChangeValidation<TValue>> validate = null)
        where TSettings : class
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (!VWorld.IsServer || binding.Scope != ConfigScope.Server)
        {
            return;
        }

        var entry = new ServerConfigBindingEntry<TSettings, TValue>(binding, validate);
        var key = new BindingRegistrationKey(binding.ChangedKey, typeof(TValue));

        lock (_lock)
        {
            _bindings[key] = entry;

            if (_registeredTypes.Add(typeof(TValue)))
            {
                VNetwork.RegisterRequestHandler<ServerConfigChangeRequest<TValue>, ServerConfigChangeResponse<TValue>>(
                    HandleRequest);
            }
        }
    }

    static ServerConfigChangeResponse<TValue> HandleRequest<TValue>(User user, ServerConfigChangeRequest<TValue> request)
    {
        if (request is null)
        {
            return new ServerConfigChangeResponse<TValue>(string.Empty, default!, false, MissingBindingReason);
        }

        if (string.IsNullOrWhiteSpace(request.BindingKey))
        {
            return new ServerConfigChangeResponse<TValue>(string.Empty, request.Value, false, "Binding key is required.");
        }

        var key = new BindingRegistrationKey(request.BindingKey, typeof(TValue));

        if (!_bindings.TryGetValue(key, out var entry) || entry is not IServerConfigBindingEntry<TValue> typedEntry)
        {
            return new ServerConfigChangeResponse<TValue>(request.BindingKey, request.Value, false, MissingBindingReason);
        }

        return typedEntry.Apply(user, request);
    }

    interface IServerConfigBindingEntry<TValue>
    {
        ServerConfigChangeResponse<TValue> Apply(User user, ServerConfigChangeRequest<TValue> request);
    }

    sealed class ServerConfigBindingEntry<TSettings, TValue> : IServerConfigBindingEntry<TValue>
        where TSettings : class
    {
        readonly Binding<TSettings, TValue> binding;
        readonly Func<User, TValue, ServerConfigChangeValidation<TValue>> validate;

        public ServerConfigBindingEntry(
            Binding<TSettings, TValue> binding,
            Func<User, TValue, ServerConfigChangeValidation<TValue>> validate)
        {
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
            this.validate = validate;
        }

        public ServerConfigChangeResponse<TValue> Apply(User user, ServerConfigChangeRequest<TValue> request)
        {
            if (!binding.TryGetValue(out var currentValue))
            {
                return new ServerConfigChangeResponse<TValue>(binding.ChangedKey, request.Value, false, ConfigUnavailableReason);
            }

            var validation = validate?.Invoke(user, request.Value)
                ?? ServerConfigChangeValidation<TValue>.Accept(request.Value);

            if (!validation.IsAccepted)
            {
                string reason = string.IsNullOrWhiteSpace(validation.RejectionReason)
                    ? "Server rejected the change request."
                    : validation.RejectionReason;

                return new ServerConfigChangeResponse<TValue>(binding.ChangedKey, validation.Value, false, reason);
            }

            if (!binding.TrySetValue(validation.Value))
            {
                return new ServerConfigChangeResponse<TValue>(binding.ChangedKey, currentValue, false, ConfigUnavailableReason);
            }

            binding.TryGetValue(out var appliedValue);
            return new ServerConfigChangeResponse<TValue>(binding.ChangedKey, appliedValue, true, string.Empty);
        }
    }

    readonly struct BindingRegistrationKey : IEquatable<BindingRegistrationKey>
    {
        readonly string bindingKey;
        readonly Type valueType;

        public BindingRegistrationKey(string bindingKey, Type valueType)
        {
            this.bindingKey = bindingKey ?? string.Empty;
            this.valueType = valueType ?? typeof(object);
        }

        public bool Equals(BindingRegistrationKey other)
            => string.Equals(bindingKey, other.bindingKey, StringComparison.Ordinal)
            && valueType == other.valueType;

        public override bool Equals(object obj)
            => obj is BindingRegistrationKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(bindingKey, valueType);
    }
}
