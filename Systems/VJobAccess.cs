namespace Emberglass.Systems;

/// <summary>
/// Declares that a job field should be treated as read-only when inferring access.
/// </summary>
/// <typeparam name="T">Wrapped handle, lookup, or resource view type.</typeparam>
public struct RO<T>
{
    /// <summary>
    /// Initializes a new wrapper with the provided value.
    /// </summary>
    /// <param name="value">Wrapped handle, lookup, or resource view.</param>
    public RO(T value)
        => Value = value;

    /// <summary>
    /// Gets or sets the wrapped handle, lookup, or resource view.
    /// </summary>
    public T Value { get; set; }
}

/// <summary>
/// Declares that a job field should be treated as read-write when inferring access.
/// </summary>
/// <typeparam name="T">Wrapped handle, lookup, or resource view type.</typeparam>
public struct RW<T>
{
    /// <summary>
    /// Initializes a new wrapper with the provided value.
    /// </summary>
    /// <param name="value">Wrapped handle, lookup, or resource view.</param>
    public RW(T value)
        => Value = value;

    /// <summary>
    /// Gets or sets the wrapped handle, lookup, or resource view.
    /// </summary>
    public T Value { get; set; }
}
