using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Emberglass.TestStubs;

/// <summary>
/// Registers a dynamic resolver for runtime-only assemblies so tests can load Emberglass without Unity assemblies.
/// </summary>
public static class StubAssemblyResolver
{
    static readonly object SyncRoot = new();
    static readonly HashSet<string> ActiveLoadNames = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, StubAssembly> StubAssemblies = new(StringComparer.OrdinalIgnoreCase);
    static readonly StubMemberCache MemberCache = new();
    static bool initialized;
    static bool enableLogging;

    /// <summary>
    /// Initializes the resolver and registers assembly resolution hooks.
    /// </summary>
    /// <param name="enableLoggingOverride">Whether to enable stub logging for diagnostics.</param>
    public static void Initialize(bool? enableLoggingOverride = null)
    {
        lock (SyncRoot)
        {
            if (initialized)
            {
                return;
            }

            enableLogging = enableLoggingOverride ?? ReadLoggingFlag();
            MemberCache.Refresh(AppDomain.CurrentDomain.GetAssemblies());

            AppDomain.CurrentDomain.AssemblyResolve += ResolveMissingAssembly;
            AppDomain.CurrentDomain.TypeResolve += ResolveMissingType;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            initialized = true;
            Log("Stub assembly resolver initialized.");
        }
    }

    /// <summary>
    /// Updates the stub member cache when a new assembly loads.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="args">The assembly load event arguments.</param>
    static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        MemberCache.Refresh(new[] { args.LoadedAssembly });
    }

    /// <summary>
    /// Resolves missing runtime-only assemblies by generating stubs on demand.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="args">The resolve event arguments.</param>
    /// <returns>The resolved assembly, or null when not handled.</returns>
    static Assembly? ResolveMissingAssembly(object? sender, ResolveEventArgs args)
    {
        AssemblyName assemblyName = new(args.Name);
        string? simpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(simpleName) || !StubAssemblyNameSelector.ShouldStubAssembly(simpleName))
        {
            return null;
        }

        Assembly? resolvedAssembly = TryLoadRealAssembly(assemblyName);
        if (resolvedAssembly is not null)
        {
            Log($"Resolved real assembly '{simpleName}'.");
            return resolvedAssembly;
        }

        StubAssembly stubAssembly = StubAssemblies.GetOrAdd(simpleName, name =>
            new StubAssembly(new AssemblyName(name), MemberCache, enableLogging));

        Log($"Stubbed missing assembly '{simpleName}'.");
        return stubAssembly.Assembly;
    }

    /// <summary>
    /// Resolves missing types by ensuring their stub assemblies define them.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="args">The resolve event arguments.</param>
    /// <returns>The resolved assembly, or null when not handled.</returns>
    static Assembly? ResolveMissingType(object? sender, ResolveEventArgs args)
    {
        string? fullTypeName = StubAssemblyNameSelector.NormalizeTypeName(args.Name);
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        string? assemblyName = StubAssemblyNameSelector.SelectAssemblyNameForType(fullTypeName);
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        StubAssembly stubAssembly = StubAssemblies.GetOrAdd(assemblyName, name =>
            new StubAssembly(new AssemblyName(name), MemberCache, enableLogging));

        stubAssembly.GetOrCreateType(fullTypeName);
        Log($"Stubbed missing type '{fullTypeName}' in '{assemblyName}'.");
        return stubAssembly.Assembly;
    }

    /// <summary>
    /// Attempts to load the real assembly before falling back to stubs.
    /// </summary>
    /// <param name="assemblyName">The assembly identity to load.</param>
    /// <returns>The loaded assembly, or null if loading fails.</returns>
    static Assembly? TryLoadRealAssembly(AssemblyName assemblyName)
    {
        string? simpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (!ActiveLoadNames.Add(simpleName))
            {
                return null;
            }
        }

        try
        {
            return Assembly.Load(assemblyName);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            lock (SyncRoot)
            {
                ActiveLoadNames.Remove(simpleName);
            }
        }
    }

    /// <summary>
    /// Reads the environment variable that enables stub logging.
    /// </summary>
    /// <returns>True when logging should be enabled.</returns>
    static bool ReadLoggingFlag()
    {
        string? flag = Environment.GetEnvironmentVariable("EMBERGLASS_TEST_STUB_LOGGING");
        return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes a diagnostic message when stub logging is enabled.
    /// </summary>
    /// <param name="message">The message to write.</param>
    static void Log(string message)
    {
        if (!enableLogging)
        {
            return;
        }

        Console.WriteLine($"[StubAssemblyResolver] {message}");
    }

    sealed class StubAssembly
    {
        readonly AssemblyBuilder assemblyBuilder;
        readonly ModuleBuilder moduleBuilder;
        readonly StubMemberCache memberCache;
        readonly bool enableStubLogging;
        readonly object syncRoot = new();
        readonly Dictionary<string, Type> createdTypes = new(StringComparer.Ordinal);
        static readonly Dictionary<string, string[]> KnownGenericValueTypes = new(StringComparer.Ordinal)
        {
            ["Unity.Entities.ComponentTypeHandle`1"] = new[] { "T" },
            ["Unity.Entities.BufferTypeHandle`1"] = new[] { "T" },
            ["Unity.Entities.ComponentLookup`1"] = new[] { "T" },
            ["Unity.Entities.BufferLookup`1"] = new[] { "T" }
        };

        /// <summary>
        /// Creates a stub assembly for a missing runtime dependency.
        /// </summary>
        /// <param name="assemblyName">The name of the stubbed assembly.</param>
        /// <param name="memberCache">The shared member cache for requested stubs.</param>
        /// <param name="enableStubLogging">Whether to emit diagnostic logging.</param>
        public StubAssembly(AssemblyName assemblyName, StubMemberCache memberCache, bool enableStubLogging)
        {
            assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            moduleBuilder = assemblyBuilder.DefineDynamicModule($"{assemblyName.Name}.dll");
            this.memberCache = memberCache;
            this.enableStubLogging = enableStubLogging;

            foreach (string typeName in StubAssemblyNameSelector.GetDefaultTypesForAssembly(assemblyName.Name ?? string.Empty))
            {
                CreateSimpleType(typeName);
            }
        }

        /// <summary>
        /// Gets the dynamic assembly instance.
        /// </summary>
        public Assembly Assembly => assemblyBuilder;

        /// <summary>
        /// Returns a cached stub type, creating it if needed.
        /// </summary>
        /// <param name="fullTypeName">The full name of the type to create.</param>
        /// <returns>The generated stub type.</returns>
        public Type GetOrCreateType(string fullTypeName)
        {
            lock (syncRoot)
            {
                if (createdTypes.TryGetValue(fullTypeName, out Type? existingType))
                {
                    return existingType;
                }

                TypeBuilder typeBuilder = DefineTypeBuilder(fullTypeName);
                if (!IsKnownValueType(fullTypeName))
                {
                    typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
                }

                MaybeEmitKnownConstructors(typeBuilder, fullTypeName);
                MaybeEmitKnownMethods(typeBuilder, fullTypeName);

                IReadOnlyList<StubMemberRequest> memberRequests = memberCache.GetRequestsForType(fullTypeName);
                StubTypeEmitter.EmitMembers(typeBuilder, memberRequests, this);

                Type createdType = typeBuilder.CreateTypeInfo()?.AsType()
                    ?? throw new InvalidOperationException($"Failed to create stub type '{fullTypeName}'.");
                createdTypes.Add(fullTypeName, createdType);
                if (enableStubLogging)
                {
                    Console.WriteLine($"[StubAssemblyResolver] Created stub type {fullTypeName} with {memberRequests.Count} members.");
                }

                return createdType;
            }
        }

        /// <summary>
        /// Creates a known type without metadata-discovered members to satisfy loader inheritance and field references.
        /// </summary>
        /// <param name="fullTypeName">The full name of the type to create.</param>
        void CreateSimpleType(string fullTypeName)
        {
            if (createdTypes.ContainsKey(fullTypeName))
            {
                return;
            }

            if (string.Equals(fullTypeName, "ProjectM.Network.NetworkIdSystem+Singleton", StringComparison.Ordinal))
            {
                CreateNetworkIdSystemType();
                return;
            }

            TypeBuilder typeBuilder = DefineTypeBuilder(fullTypeName);
            if (!IsKnownValueType(fullTypeName))
            {
                typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            }

            MaybeEmitKnownConstructors(typeBuilder, fullTypeName);
            MaybeEmitKnownMethods(typeBuilder, fullTypeName);
            Type createdType = typeBuilder.CreateTypeInfo()?.AsType()
                ?? throw new InvalidOperationException($"Failed to create stub type '{fullTypeName}'.");
            createdTypes.Add(fullTypeName, createdType);
            if (enableStubLogging)
            {
                Console.WriteLine($"[StubAssemblyResolver] Created simple stub type {fullTypeName}.");
            }
        }

        /// <summary>
        /// Creates the nested ProjectM.NetworkIdSystem.Singleton type required by VWorld static fields.
        /// </summary>
        void CreateNetworkIdSystemType()
        {
            const string ParentTypeName = "ProjectM.Network.NetworkIdSystem";
            const string NestedTypeName = "ProjectM.Network.NetworkIdSystem+Singleton";

            if (createdTypes.ContainsKey(NestedTypeName))
            {
                return;
            }

            TypeBuilder parentBuilder = moduleBuilder.DefineType(ParentTypeName, TypeAttributes.Public | TypeAttributes.Class);
            parentBuilder.DefineDefaultConstructor(MethodAttributes.Public);

            TypeBuilder singletonBuilder = parentBuilder.DefineNestedType(
                "Singleton",
                TypeAttributes.NestedPublic | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(ValueType));

            Type singletonType = singletonBuilder.CreateTypeInfo()?.AsType()
                ?? throw new InvalidOperationException($"Failed to create stub type '{NestedTypeName}'.");
            Type parentType = parentBuilder.CreateTypeInfo()?.AsType()
                ?? throw new InvalidOperationException($"Failed to create stub type '{ParentTypeName}'.");

            createdTypes.Add(ParentTypeName, parentType);
            createdTypes.Add(NestedTypeName, singletonType);
            createdTypes.Add("ProjectM.Network.NetworkIdSystem.Singleton", singletonType);
            if (enableStubLogging)
            {
                Console.WriteLine($"[StubAssemblyResolver] Created simple stub type {NestedTypeName}.");
            }
        }

        /// <summary>
        /// Emits small hand-written constructors for Unity types used by static initializers.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="fullTypeName">The full type name being generated.</param>
        static void MaybeEmitKnownConstructors(TypeBuilder typeBuilder, string fullTypeName)
        {
            if (string.Equals(fullTypeName, "UnityEngine.WaitForSeconds", StringComparison.Ordinal))
            {
                ConstructorBuilder constructor = typeBuilder.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    new[] { typeof(float) });
                ILGenerator il = constructor.GetILGenerator();
                il.Emit(OpCodes.Ret);
                return;
            }

            if (string.Equals(fullTypeName, "BepInEx.BepInPluginAttribute", StringComparison.Ordinal))
            {
                ConstructorBuilder constructor = typeBuilder.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    new[] { typeof(string), typeof(string), typeof(string) });
                ILGenerator il = constructor.GetILGenerator();
                il.Emit(OpCodes.Ret);
            }
        }

        /// <summary>
        /// Emits small hand-written methods for runtime probes used by tests.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="fullTypeName">The full type name being generated.</param>
        void MaybeEmitKnownMethods(TypeBuilder typeBuilder, string fullTypeName)
        {
            if (!string.Equals(fullTypeName, "ProjectM.WorldUtility", StringComparison.Ordinal))
            {
                return;
            }

            Type worldType = ResolveType(new StubTypeReference("Unity.Entities.World"));
            EmitNullWorldMethod(typeBuilder, "FindClientWorld", worldType);
            EmitNullWorldMethod(typeBuilder, "FindServerWorld", worldType);
        }

        /// <summary>
        /// Emits a static world lookup method that returns null in tests.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="methodName">Method name to emit.</param>
        /// <param name="worldType">The Unity.Entities.World stub type.</param>
        static void EmitNullWorldMethod(TypeBuilder typeBuilder, string methodName, Type worldType)
        {
            MethodBuilder method = typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                worldType,
                new[] { typeof(bool) });
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Defines a class or known value-type stub builder.
        /// </summary>
        /// <param name="fullTypeName">The full type name.</param>
        /// <returns>The type builder.</returns>
        TypeBuilder DefineTypeBuilder(string fullTypeName)
        {
            if (IsKnownValueType(fullTypeName))
            {
                TypeBuilder valueTypeBuilder = moduleBuilder.DefineType(
                    fullTypeName,
                    TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                    typeof(ValueType));

                if (KnownGenericValueTypes.TryGetValue(fullTypeName, out string[]? genericParameterNames))
                {
                    valueTypeBuilder.DefineGenericParameters(genericParameterNames);
                }

                return valueTypeBuilder;
            }

            return moduleBuilder.DefineType(fullTypeName, TypeAttributes.Public | TypeAttributes.Class);
        }

        /// <summary>
        /// Determines whether a generated stub must be emitted as a value type.
        /// </summary>
        /// <param name="fullTypeName">The full type name.</param>
        /// <returns>True when the type should be a value type.</returns>
        static bool IsKnownValueType(string fullTypeName)
            => string.Equals(fullTypeName, "ProjectM.Network.User", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "Unity.Entities.Entity", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "Unity.Entities.ArchetypeChunk", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "Unity.Entities.EntityTypeHandle", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "Unity.Entities.EntityStorageInfoLookup", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "Unity.Entities.EntityQueryOptions", StringComparison.Ordinal)
                || KnownGenericValueTypes.ContainsKey(fullTypeName)
                || string.Equals(fullTypeName, "ProjectM.Network.NetworkIdSystem+Singleton", StringComparison.Ordinal)
                || string.Equals(fullTypeName, "ProjectM.Network.NetworkIdSystem.Singleton", StringComparison.Ordinal);

        /// <summary>
        /// Resolves a type reference to a real or stub type instance.
        /// </summary>
        /// <param name="typeReference">The type reference to resolve.</param>
        /// <returns>The resolved type.</returns>
        public Type ResolveType(StubTypeReference typeReference)
        {
            Type? resolved = StubTypeResolver.ResolveCommonType(typeReference);
            if (resolved is not null)
            {
                return resolved;
            }

            string? fullName = typeReference.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return typeof(object);
            }

            return GetOrCreateType(fullName);
        }
    }

    static class StubTypeEmitter
    {
        /// <summary>
        /// Emits requested members for a stubbed type.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="memberRequests">The requested members to emit.</param>
        /// <param name="stubAssembly">The owning stub assembly.</param>
        public static void EmitMembers(TypeBuilder typeBuilder, IReadOnlyList<StubMemberRequest> memberRequests, StubAssembly stubAssembly)
        {
            foreach (StubMemberRequest memberRequest in memberRequests)
            {
                switch (memberRequest.Kind)
                {
                    case StubMemberKind.Field:
                        EmitField(typeBuilder, memberRequest, stubAssembly);
                        break;
                    case StubMemberKind.Property:
                        EmitProperty(typeBuilder, memberRequest, stubAssembly);
                        break;
                    case StubMemberKind.Method:
                        EmitMethod(typeBuilder, memberRequest, stubAssembly);
                        break;
                }
            }
        }

        /// <summary>
        /// Emits a public field stub for the requested member.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="memberRequest">The member request metadata.</param>
        /// <param name="stubAssembly">The owning stub assembly.</param>
        static void EmitField(TypeBuilder typeBuilder, StubMemberRequest memberRequest, StubAssembly stubAssembly)
        {
            Type fieldType = stubAssembly.ResolveType(memberRequest.Signature.ReturnType);
            typeBuilder.DefineField(memberRequest.Name, fieldType, FieldAttributes.Public);
        }

        /// <summary>
        /// Emits a public property with empty accessors for the requested member.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="memberRequest">The member request metadata.</param>
        /// <param name="stubAssembly">The owning stub assembly.</param>
        static void EmitProperty(TypeBuilder typeBuilder, StubMemberRequest memberRequest, StubAssembly stubAssembly)
        {
            Type propertyType = stubAssembly.ResolveType(memberRequest.Signature.ReturnType);
            PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(memberRequest.Name, PropertyAttributes.None, propertyType, Type.EmptyTypes);

            MethodBuilder getter = typeBuilder.DefineMethod(
                $"get_{memberRequest.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                propertyType,
                Type.EmptyTypes);
            EmitEmptyBody(getter.GetILGenerator(), propertyType);
            propertyBuilder.SetGetMethod(getter);

            MethodBuilder setter = typeBuilder.DefineMethod(
                $"set_{memberRequest.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(void),
                new[] { propertyType });
            EmitEmptyBody(setter.GetILGenerator(), typeof(void));
            propertyBuilder.SetSetMethod(setter);
        }

        /// <summary>
        /// Emits a public method with an empty body for the requested member.
        /// </summary>
        /// <param name="typeBuilder">The type builder to populate.</param>
        /// <param name="memberRequest">The member request metadata.</param>
        /// <param name="stubAssembly">The owning stub assembly.</param>
        static void EmitMethod(TypeBuilder typeBuilder, StubMemberRequest memberRequest, StubAssembly stubAssembly)
        {
            if (string.Equals(memberRequest.Name, ".ctor", StringComparison.Ordinal))
            {
                return;
            }

            Type returnType = stubAssembly.ResolveType(memberRequest.Signature.ReturnType);
            Type[] parameterTypes = memberRequest.Signature.ParameterTypes
                .Select(stubAssembly.ResolveType)
                .ToArray();

            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                memberRequest.Name,
                MethodAttributes.Public | MethodAttributes.HideBySig,
                returnType,
                parameterTypes);

            EmitEmptyBody(methodBuilder.GetILGenerator(), returnType);
        }

        /// <summary>
        /// Emits a default-returning method body for the given return type.
        /// </summary>
        /// <param name="ilGenerator">The IL generator to emit to.</param>
        /// <param name="returnType">The return type for the method.</param>
        static void EmitEmptyBody(ILGenerator ilGenerator, Type returnType)
        {
            if (returnType == typeof(void))
            {
                ilGenerator.Emit(OpCodes.Ret);
                return;
            }

            if (returnType.IsValueType)
            {
                LocalBuilder local = ilGenerator.DeclareLocal(returnType);
                ilGenerator.Emit(OpCodes.Ldloca_S, local);
                ilGenerator.Emit(OpCodes.Initobj, returnType);
                ilGenerator.Emit(OpCodes.Ldloc, local);
                ilGenerator.Emit(OpCodes.Ret);
                return;
            }

            ilGenerator.Emit(OpCodes.Ldnull);
            ilGenerator.Emit(OpCodes.Ret);
        }
    }

    sealed class StubMemberCache
    {
        readonly object syncRoot = new();
        readonly Dictionary<string, List<StubMemberRequest>> memberRequests = new(StringComparer.Ordinal);

        /// <summary>
        /// Retrieves any requested members for a given type name.
        /// </summary>
        /// <param name="fullTypeName">The full type name.</param>
        /// <returns>The list of requested members for the type.</returns>
        public IReadOnlyList<StubMemberRequest> GetRequestsForType(string fullTypeName)
        {
            lock (syncRoot)
            {
                if (memberRequests.TryGetValue(fullTypeName, out List<StubMemberRequest>? requests))
                {
                    return requests;
                }

                return Array.Empty<StubMemberRequest>();
            }
        }

        /// <summary>
        /// Scans assemblies to discover referenced members that need stubs.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan.</param>
        public void Refresh(IEnumerable<Assembly> assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                string? location = TryGetAssemblyLocation(assembly);
                if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
                {
                    continue;
                }

                using FileStream fileStream = File.OpenRead(location);
                using PEReader peReader = new(fileStream);
                if (!peReader.HasMetadata)
                {
                    continue;
                }

                MetadataReader metadataReader = peReader.GetMetadataReader();
                StubMetadataDiscovery.DiscoverMemberRequests(metadataReader, AddRequest);
            }
        }

        /// <summary>
        /// Gets the assembly location when available, skipping dynamic assemblies.
        /// </summary>
        /// <param name="assembly">Assembly to inspect.</param>
        /// <returns>The assembly location, or null when unavailable.</returns>
        static string? TryGetAssemblyLocation(Assembly assembly)
        {
            try
            {
                return assembly.IsDynamic ? null : assembly.Location;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Adds a member request entry for a type.
        /// </summary>
        /// <param name="typeName">The type name to store under.</param>
        /// <param name="request">The requested member metadata.</param>
        void AddRequest(string typeName, StubMemberRequest request)
        {
            lock (syncRoot)
            {
                if (!memberRequests.TryGetValue(typeName, out List<StubMemberRequest>? requests))
                {
                    requests = new List<StubMemberRequest>();
                    memberRequests.Add(typeName, requests);
                }

                requests.Add(request);
            }
        }
    }

    static class StubMetadataDiscovery
    {
        /// <summary>
        /// Discovers member references that require stub generation.
        /// </summary>
        /// <param name="metadataReader">The metadata reader for the assembly.</param>
        /// <param name="addRequest">Callback to record stub member requests.</param>
        public static void DiscoverMemberRequests(MetadataReader metadataReader, Action<string, StubMemberRequest> addRequest)
        {
            StubSignatureTypeProvider typeProvider = new(metadataReader);

            foreach (MemberReferenceHandle memberHandle in metadataReader.MemberReferences)
            {
                MemberReference memberReference = metadataReader.GetMemberReference(memberHandle);
                string memberName = metadataReader.GetString(memberReference.Name);
                string? declaringTypeName = GetDeclaringTypeName(metadataReader, memberReference.Parent);
                if (declaringTypeName is null || !StubAssemblyNameSelector.ShouldStubType(declaringTypeName))
                {
                    continue;
                }

                BlobReader signatureReader = metadataReader.GetBlobReader(memberReference.Signature);
                BlobReader signatureHeaderReader = signatureReader;
                SignatureHeader signatureHeader = signatureHeaderReader.ReadSignatureHeader();
                StubMemberRequest? request = signatureHeader.Kind switch
                {
                    SignatureKind.Method => CreateMethodRequest(signatureReader, typeProvider, memberName),
                    SignatureKind.Field => CreateFieldRequest(signatureReader, typeProvider, memberName),
                    _ => null
                };

                if (request is null)
                {
                    continue;
                }

                addRequest(declaringTypeName, request);
                MaybeAddPropertyRequest(addRequest, declaringTypeName, request);
            }
        }

        /// <summary>
        /// Creates a stub request for a field member signature.
        /// </summary>
        /// <param name="signatureReader">The signature reader.</param>
        /// <param name="typeProvider">The signature type provider.</param>
        /// <param name="memberName">The field name.</param>
        /// <returns>The stub member request.</returns>
        static StubMemberRequest CreateFieldRequest(BlobReader signatureReader, StubSignatureTypeProvider typeProvider, string memberName)
        {
            SignatureDecoder<StubTypeReference, object?> decoder = new(typeProvider, typeProvider.MetadataReader, null);
            StubTypeReference fieldType = decoder.DecodeFieldSignature(ref signatureReader);
            StubMemberSignature signature = new(fieldType, Array.Empty<StubTypeReference>());
            return new StubMemberRequest(StubMemberKind.Field, memberName, signature);
        }

        /// <summary>
        /// Creates a stub request for a method member signature.
        /// </summary>
        /// <param name="signatureReader">The signature reader.</param>
        /// <param name="typeProvider">The signature type provider.</param>
        /// <param name="memberName">The method name.</param>
        /// <returns>The stub member request.</returns>
        static StubMemberRequest CreateMethodRequest(BlobReader signatureReader, StubSignatureTypeProvider typeProvider, string memberName)
        {
            SignatureDecoder<StubTypeReference, object?> decoder = new(typeProvider, typeProvider.MetadataReader, null);
            MethodSignature<StubTypeReference> methodSignature = decoder.DecodeMethodSignature(ref signatureReader);
            StubMemberSignature signature = new(methodSignature.ReturnType, methodSignature.ParameterTypes.ToArray());
            return new StubMemberRequest(StubMemberKind.Method, memberName, signature);
        }

        /// <summary>
        /// Adds a property stub request when a getter or setter method is referenced.
        /// </summary>
        /// <param name="addRequest">Callback to record stub member requests.</param>
        /// <param name="typeName">The declaring type name.</param>
        /// <param name="request">The method request to inspect.</param>
        static void MaybeAddPropertyRequest(Action<string, StubMemberRequest> addRequest, string typeName, StubMemberRequest request)
        {
            if (request.Kind != StubMemberKind.Method)
            {
                return;
            }

            if (request.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                string propertyName = request.Name[4..];
                addRequest(typeName, new StubMemberRequest(StubMemberKind.Property, propertyName, request.Signature));
            }
            else if (request.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                string propertyName = request.Name[4..];
                StubTypeReference propertyType = request.Signature.ParameterTypes.FirstOrDefault() ?? new StubTypeReference("System.Object");
                addRequest(typeName, new StubMemberRequest(StubMemberKind.Property, propertyName, new StubMemberSignature(propertyType, Array.Empty<StubTypeReference>())));
            }
        }

        /// <summary>
        /// Resolves a declaring type name from a metadata handle.
        /// </summary>
        /// <param name="metadataReader">The metadata reader.</param>
        /// <param name="parentHandle">The parent handle.</param>
        /// <returns>The full type name when available.</returns>
        static string? GetDeclaringTypeName(MetadataReader metadataReader, EntityHandle parentHandle)
        {
            return parentHandle.Kind switch
            {
                HandleKind.TypeReference => GetTypeReferenceName(metadataReader, metadataReader.GetTypeReference((TypeReferenceHandle)parentHandle)),
                HandleKind.TypeSpecification => null,
                HandleKind.TypeDefinition => GetTypeDefinitionName(metadataReader, metadataReader.GetTypeDefinition((TypeDefinitionHandle)parentHandle)),
                _ => null
            };
        }

        /// <summary>
        /// Formats a full name from a type reference.
        /// </summary>
        /// <param name="metadataReader">The metadata reader.</param>
        /// <param name="typeReference">The type reference.</param>
        /// <returns>The full type name.</returns>
        public static string GetTypeReferenceName(MetadataReader metadataReader, TypeReference typeReference)
        {
            string typeName = metadataReader.GetString(typeReference.Name);
            string typeNamespace = metadataReader.GetString(typeReference.Namespace);
            return string.IsNullOrWhiteSpace(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";
        }

        /// <summary>
        /// Formats a full name from a type definition.
        /// </summary>
        /// <param name="metadataReader">The metadata reader.</param>
        /// <param name="typeDefinition">The type definition.</param>
        /// <returns>The full type name.</returns>
        public static string GetTypeDefinitionName(MetadataReader metadataReader, TypeDefinition typeDefinition)
        {
            string typeName = metadataReader.GetString(typeDefinition.Name);
            string typeNamespace = metadataReader.GetString(typeDefinition.Namespace);
            return string.IsNullOrWhiteSpace(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";
        }
    }

    sealed class StubSignatureTypeProvider : ISignatureTypeProvider<StubTypeReference, object?>
    {
        readonly MetadataReader metadataReader;

        /// <summary>
        /// Initializes the metadata signature type provider.
        /// </summary>
        /// <param name="metadataReader">The metadata reader to resolve names from.</param>
        public StubSignatureTypeProvider(MetadataReader metadataReader)
        {
            this.metadataReader = metadataReader;
        }

        /// <summary>
        /// Gets the metadata reader used by the signature decoder.
        /// </summary>
        public MetadataReader MetadataReader => metadataReader;

        /// <summary>
        /// Creates a type reference for an array signature.
        /// </summary>
        /// <param name="elementType">The element type.</param>
        /// <param name="shape">The array shape.</param>
        /// <returns>The array type reference.</returns>
        public StubTypeReference GetArrayType(StubTypeReference elementType, ArrayShape shape)
        {
            return elementType.WithArrayRank(shape.Rank);
        }

        /// <summary>
        /// Creates a by-reference type reference.
        /// </summary>
        /// <param name="elementType">The element type.</param>
        /// <returns>The by-reference type reference.</returns>
        public StubTypeReference GetByReferenceType(StubTypeReference elementType)
        {
            return elementType.WithByReference();
        }

        /// <summary>
        /// Maps function pointer signatures to IntPtr.
        /// </summary>
        /// <param name="signature">The function pointer signature.</param>
        /// <returns>An IntPtr type reference.</returns>
        public StubTypeReference GetFunctionPointerType(MethodSignature<StubTypeReference> signature)
        {
            return new StubTypeReference("System.IntPtr");
        }

        /// <summary>
        /// Creates a generic instantiation type reference.
        /// </summary>
        /// <param name="genericType">The generic type definition.</param>
        /// <param name="typeArguments">The generic arguments.</param>
        /// <returns>The generic instantiation type reference.</returns>
        public StubTypeReference GetGenericInstantiation(StubTypeReference genericType, ImmutableArray<StubTypeReference> typeArguments)
        {
            return genericType.WithGenericArguments(typeArguments.ToArray());
        }

        /// <summary>
        /// Resolves a generic method parameter.
        /// </summary>
        /// <param name="genericContext">The generic context.</param>
        /// <param name="index">The parameter index.</param>
        /// <returns>A placeholder type reference.</returns>
        public StubTypeReference GetGenericMethodParameter(object? genericContext, int index)
        {
            return new StubTypeReference("System.Object");
        }

        /// <summary>
        /// Resolves a generic type parameter.
        /// </summary>
        /// <param name="genericContext">The generic context.</param>
        /// <param name="index">The parameter index.</param>
        /// <returns>A placeholder type reference.</returns>
        public StubTypeReference GetGenericTypeParameter(object? genericContext, int index)
        {
            return new StubTypeReference("System.Object");
        }

        /// <summary>
        /// Ignores custom modifiers and returns the unmodified type.
        /// </summary>
        /// <param name="modifier">The modifier type.</param>
        /// <param name="unmodifiedType">The unmodified type.</param>
        /// <param name="isRequired">Whether the modifier is required.</param>
        /// <returns>The unmodified type reference.</returns>
        public StubTypeReference GetModifiedType(StubTypeReference modifier, StubTypeReference unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        /// <summary>
        /// Returns a pinned type reference.
        /// </summary>
        /// <param name="elementType">The element type.</param>
        /// <returns>The pinned type reference.</returns>
        public StubTypeReference GetPinnedType(StubTypeReference elementType)
        {
            return elementType;
        }

        /// <summary>
        /// Maps pointer types to IntPtr for stubs.
        /// </summary>
        /// <param name="elementType">The element type.</param>
        /// <returns>An IntPtr type reference.</returns>
        public StubTypeReference GetPointerType(StubTypeReference elementType)
        {
            return new StubTypeReference("System.IntPtr");
        }

        /// <summary>
        /// Resolves primitive type codes to their full names.
        /// </summary>
        /// <param name="typeCode">The primitive type code.</param>
        /// <returns>The primitive type reference.</returns>
        public StubTypeReference GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return new StubTypeReference(typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Void => "System.Void",
                _ => "System.Object"
            });
        }

        /// <summary>
        /// Creates an SZ-array type reference.
        /// </summary>
        /// <param name="elementType">The element type.</param>
        /// <returns>The array type reference.</returns>
        public StubTypeReference GetSZArrayType(StubTypeReference elementType)
        {
            return elementType.WithArrayRank(1);
        }

        /// <summary>
        /// Resolves a type definition handle into a type reference.
        /// </summary>
        /// <param name="reader">The metadata reader.</param>
        /// <param name="handle">The type definition handle.</param>
        /// <param name="rawTypeKind">The raw type kind.</param>
        /// <returns>The type reference.</returns>
        public StubTypeReference GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            TypeDefinition typeDefinition = metadataReader.GetTypeDefinition(handle);
            return new StubTypeReference(StubMetadataDiscovery.GetTypeDefinitionName(metadataReader, typeDefinition));
        }

        /// <summary>
        /// Resolves a type reference handle into a type reference.
        /// </summary>
        /// <param name="reader">The metadata reader.</param>
        /// <param name="handle">The type reference handle.</param>
        /// <param name="rawTypeKind">The raw type kind.</param>
        /// <returns>The type reference.</returns>
        public StubTypeReference GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference typeReference = metadataReader.GetTypeReference(handle);
            return new StubTypeReference(StubMetadataDiscovery.GetTypeReferenceName(metadataReader, typeReference));
        }

        /// <summary>
        /// Resolves a type specification handle into a type reference.
        /// </summary>
        /// <param name="reader">The metadata reader.</param>
        /// <param name="genericContext">The generic context.</param>
        /// <param name="handle">The type specification handle.</param>
        /// <param name="rawTypeKind">The raw type kind.</param>
        /// <returns>The type reference.</returns>
        public StubTypeReference GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            TypeSpecification typeSpecification = metadataReader.GetTypeSpecification(handle);
            BlobReader signatureReader = metadataReader.GetBlobReader(typeSpecification.Signature);
            SignatureDecoder<StubTypeReference, object?> decoder = new(this, metadataReader, genericContext);
            return decoder.DecodeType(ref signatureReader);
        }
    }

    sealed record StubMemberRequest(StubMemberKind Kind, string Name, StubMemberSignature Signature);

    enum StubMemberKind
    {
        Field,
        Property,
        Method
    }

    sealed record StubMemberSignature(StubTypeReference ReturnType, IReadOnlyList<StubTypeReference> ParameterTypes);

    sealed record StubTypeReference(string FullName)
    {
        public int ArrayRank { get; init; }
        public bool IsByReference { get; init; }
        public IReadOnlyList<StubTypeReference> GenericArguments { get; init; } = Array.Empty<StubTypeReference>();

        /// <summary>
        /// Creates a copy with the specified array rank.
        /// </summary>
        /// <param name="rank">The array rank.</param>
        /// <returns>The updated type reference.</returns>
        public StubTypeReference WithArrayRank(int rank)
        {
            return this with { ArrayRank = rank };
        }

        /// <summary>
        /// Creates a copy marked as by-reference.
        /// </summary>
        /// <returns>The updated type reference.</returns>
        public StubTypeReference WithByReference()
        {
            return this with { IsByReference = true };
        }

        /// <summary>
        /// Creates a copy with the provided generic arguments.
        /// </summary>
        /// <param name="arguments">The generic argument list.</param>
        /// <returns>The updated type reference.</returns>
        public StubTypeReference WithGenericArguments(IReadOnlyList<StubTypeReference> arguments)
        {
            return this with { GenericArguments = arguments };
        }
    }

    static class StubTypeResolver
    {
        /// <summary>
        /// Resolves a type reference to either a real or stubbed type.
        /// </summary>
        /// <param name="typeReference">The type reference to resolve.</param>
        /// <returns>The resolved type.</returns>
        public static Type? ResolveCommonType(StubTypeReference typeReference)
        {
            string? fullName = typeReference.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return typeof(object);
            }

            if (!StubAssemblyNameSelector.ShouldStubType(fullName))
            {
                Type? resolved = Type.GetType(fullName, false);
                return resolved is null
                    ? typeof(object)
                    : ApplyModifiers(resolved, typeReference);
            }

            string? assemblyName = StubAssemblyNameSelector.SelectAssemblyNameForType(fullName);
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return typeof(object);
            }

            StubAssembly stubAssembly = StubAssemblies.GetOrAdd(assemblyName, name =>
                new StubAssembly(new AssemblyName(name), MemberCache, enableLogging));

            Type stubType = stubAssembly.GetOrCreateType(fullName);
            return ApplyModifiers(stubType, typeReference);
        }

        /// <summary>
        /// Applies array and by-reference modifiers to a resolved type.
        /// </summary>
        /// <param name="resolved">The resolved type.</param>
        /// <param name="typeReference">The reference containing modifiers.</param>
        /// <returns>The modified type.</returns>
        static Type ApplyModifiers(Type resolved, StubTypeReference typeReference)
        {
            Type current = resolved;
            if (typeReference.IsByReference)
            {
                current = current.MakeByRefType();
            }

            if (typeReference.ArrayRank > 0)
            {
                current = typeReference.ArrayRank == 1
                    ? current.MakeArrayType()
                    : current.MakeArrayType(typeReference.ArrayRank);
            }

            return current;
        }
    }

    static class StubAssemblyNameSelector
    {
        static readonly string[] AssemblyPrefixes =
        {
            "BepInEx",
            "UnityEngine",
            "UnityEditor",
            "ProjectM",
            "Il2CppSystem",
            "Il2Cpp",
            "Unity"
        };

        /// <summary>
        /// Determines whether an assembly name should be stubbed.
        /// </summary>
        /// <param name="assemblyName">The assembly name to evaluate.</param>
        /// <returns>True when the assembly should be stubbed.</returns>
        public static bool ShouldStubAssembly(string assemblyName)
        {
            return AssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a type name belongs to a stubbed assembly prefix.
        /// </summary>
        /// <param name="fullTypeName">The full type name.</param>
        /// <returns>True when the type should be stubbed.</returns>
        public static bool ShouldStubType(string fullTypeName)
        {
            if (string.Equals(fullTypeName, "Singleton", StringComparison.Ordinal))
            {
                return true;
            }

            return AssemblyPrefixes.Any(prefix => fullTypeName.StartsWith(prefix + ".", StringComparison.Ordinal));
        }

        /// <summary>
        /// Selects a stub assembly name based on a full type name.
        /// </summary>
        /// <param name="fullTypeName">The full type name.</param>
        /// <returns>The assembly name to use, or null when not stubbed.</returns>
        public static string? SelectAssemblyNameForType(string fullTypeName)
        {
            if (string.Equals(fullTypeName, "Singleton", StringComparison.Ordinal))
            {
                return "ProjectM.Shared";
            }

            if (fullTypeName.StartsWith("UnityEngine.", StringComparison.Ordinal))
            {
                return "UnityEngine.CoreModule";
            }

            if (fullTypeName.StartsWith("Unity.Entities.", StringComparison.Ordinal))
            {
                return "Unity.Entities";
            }

            if (fullTypeName.StartsWith("BepInEx.Unity.IL2CPP.", StringComparison.Ordinal))
            {
                return "BepInEx.Unity.IL2CPP";
            }

            if (fullTypeName.StartsWith("BepInEx.", StringComparison.Ordinal))
            {
                return "BepInEx.Core";
            }

            if (fullTypeName.StartsWith("ProjectM.", StringComparison.Ordinal))
            {
                return "ProjectM.Shared";
            }

            foreach (string prefix in AssemblyPrefixes)
            {
                if (fullTypeName.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return prefix;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns common types that must exist immediately when a stub assembly is loaded.
        /// </summary>
        /// <param name="assemblyName">The stub assembly name.</param>
        /// <returns>Default type names to define.</returns>
        public static IReadOnlyList<string> GetDefaultTypesForAssembly(string assemblyName)
        {
            return assemblyName switch
            {
                "BepInEx.Core" => new[] { "BepInEx.Paths", "BepInEx.BepInPluginAttribute", "BepInEx.Logging.ManualLogSource" },
                "BepInEx.Unity.IL2CPP" => new[] { "BepInEx.Unity.IL2CPP.BasePlugin" },
                "Unity.Entities" => new[]
                {
                    "Unity.Entities.Entity",
                    "Unity.Entities.ArchetypeChunk",
                    "Unity.Entities.EntityTypeHandle",
                    "Unity.Entities.EntityStorageInfoLookup",
                    "Unity.Entities.ComponentTypeHandle`1",
                    "Unity.Entities.BufferTypeHandle`1",
                    "Unity.Entities.ComponentLookup`1",
                    "Unity.Entities.BufferLookup`1",
                    "Unity.Entities.World",
                    "Unity.Entities.EntityManager",
                    "Unity.Entities.ComponentSystemBase",
                    "Unity.Entities.EntityQueryOptions"
                },
                "UnityEngine.CoreModule" => new[] { "UnityEngine.MonoBehaviour", "UnityEngine.WaitForSeconds" },
                "ProjectM.Shared" => new[] { "ProjectM.WorldUtility", "ProjectM.Network.NetworkIdSystem+Singleton", "Singleton", "ProjectM.Network.User" },
                _ => Array.Empty<string>()
            };
        }

        /// <summary>
        /// Normalizes a raw type name by stripping assembly qualifiers.
        /// </summary>
        /// <param name="rawTypeName">The raw type name.</param>
        /// <returns>The normalized type name.</returns>
        public static string? NormalizeTypeName(string rawTypeName)
        {
            string[] parts = rawTypeName.Split(',', 2, StringSplitOptions.TrimEntries);
            return parts[0];
        }
    }
}
