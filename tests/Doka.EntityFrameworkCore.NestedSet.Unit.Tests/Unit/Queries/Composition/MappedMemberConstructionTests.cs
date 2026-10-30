using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks deferred member construction, metadata isolation and the lifetime of mapped query reads.</summary>
[Collection("Allocation measurements")]
public sealed class MappedMemberConstructionTests
{
    /// <summary>Mapped reads bind each compatible alias and retain markers for unrelated aliases.</summary>
    [Fact]
    public void MappedMemberUsesTheCurrentAliasWithoutReadingItsClrValue()
    {
        // Arrange
        var (_, _, property) = RuntimeProperty();
        var first = Expression.Parameter(typeof(MemberNode), "first");
        var second = Expression.Parameter(typeof(DerivedMemberNode), "second");
        var unrelated = Expression.Parameter(typeof(UnrelatedNode), "unrelated");
        _ = NestedSetExpressions.QueryProperty(first, property);

        // Act
        var compatible = NestedSetExpressions.QueryProperty(second, property);
        var incompatible = NestedSetExpressions.QueryProperty(unrelated, property);

        // Assert
        Assert.Same(second, Assert.IsAssignableFrom<MemberExpression>(compatible).Expression);
        Assert.Same(property.PropertyInfo, Assert.IsAssignableFrom<MemberExpression>(compatible).Member);
        var marker = Assert.IsAssignableFrom<MethodCallExpression>(incompatible);
        Assert.Same(unrelated, marker.Arguments[0]);
        Assert.Equal(nameof(MemberNode.Value), Assert.IsType<ConstantExpression>(marker.Arguments[1]).Value);
    }

    /// <summary>Equal property names in independent mappings retain their own CLR and shadow storage.</summary>
    /// <param name="sameModel">Whether the two named mappings also share a model instance.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappedMemberDoesNotCrossNamedMappingsOrModels(bool sameModel)
    {
        // Arrange
        var (model, _, mapped) = RuntimeProperty();
        var otherModel = sameModel ? model : NewModel();
        var other = otherModel.AddEntityType("Other", typeof(MemberNode), sharedClrType: true);
        var shadow = other.AddProperty(nameof(MemberNode.Value), typeof(int));
        var alias = Expression.Parameter(typeof(MemberNode), "node");
        _ = NestedSetExpressions.QueryProperty(alias, mapped);

        // Act
        var direct = NestedSetExpressions.QueryProperty(alias, mapped);
        var indirect = NestedSetExpressions.QueryProperty(alias, shadow);

        // Assert
        Assert.NotSame(mapped, shadow);
        Assert.Same(mapped.PropertyInfo, Assert.IsAssignableFrom<MemberExpression>(direct).Member);
        Assert.Equal(nameof(MemberNode.Value), Assert.IsType<ConstantExpression>(
            Assert.IsAssignableFrom<MethodCallExpression>(indirect).Arguments[1]).Value);
    }

    /// <summary>Design-time and convention metadata observe a removed property mapping on the next call.</summary>
    /// <param name="convention">Whether removal uses EF's convention metadata interface.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableMetadataIsInspectedAgainAfterItsMappingIsRemoved(bool convention)
    {
        // Arrange
        var builder = new ModelBuilder(new ConventionSet());
        var entity = builder.Model.AddEntityType(typeof(MemberNode));
        var property = entity.AddProperty(nameof(MemberNode.Stored), typeof(int));
        var alias = Expression.Parameter(typeof(MemberNode), "node");
        var before = NestedSetExpressions.QueryProperty(alias, (IProperty)property);

        // Act
        if (convention)
        {
            ((IConventionEntityType)entity).RemoveProperty(property.Name);
        }
        else
        {
            entity.RemoveProperty(property.Name);
        }

        var after = NestedSetExpressions.QueryProperty(alias, (IProperty)property);

        // Assert
        Assert.Equal(nameof(MemberNode.Stored), Assert.IsAssignableFrom<MemberExpression>(before).Member.Name);
        Assert.IsAssignableFrom<MethodCallExpression>(after);
    }

    /// <summary>Custom metadata wrappers retain their per-call property inspection.</summary>
    [Fact]
    public void CustomMetadataKeepsItsPerCallInspection()
    {
        // Arrange
        var (_, _, property) = RuntimeProperty();
        var proxy = DispatchProxy.Create<IProperty, MetadataProbe>();
        var probe = (MetadataProbe)proxy;
        probe.Target = property;
        var alias = Expression.Parameter(typeof(MemberNode), "node");

        // Act
        var first = NestedSetExpressions.QueryProperty(alias, proxy);
        var second = NestedSetExpressions.QueryProperty(alias, proxy);

        // Assert
        Assert.IsAssignableFrom<MethodCallExpression>(first);
        Assert.IsAssignableFrom<MethodCallExpression>(second);
        Assert.Equal(2, probe.PropertyReads);
    }

    /// <summary>Custom reflection objects and custom declaring types can change their answer between calls.</summary>
    /// <param name="customDeclaringType">Whether assignability comes from a custom Type wrapper.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomReflectionIsNeverTreatedAsImmutable(bool customDeclaringType)
    {
        // Arrange
        var model = NewModel();
        var entity = model.AddEntityType("Custom", typeof(MemberNode));
        var declaringType = new MutableDeclaringType(typeof(MemberNode));
        var member = new MutableProperty(typeof(MemberNode).GetProperty(nameof(MemberNode.Value))!,
            customDeclaringType ? declaringType : typeof(MemberNode));

        var property = entity.AddProperty(nameof(MemberNode.Value), typeof(int), propertyInfo: member);
        var alias = Expression.Parameter(typeof(MemberNode), "node");
        var before = NestedSetExpressions.QueryProperty(alias, property);

        // Act
        if (customDeclaringType)
        {
            declaringType.AcceptsAlias = false;
        }
        else
        {
            member.ReportedName = nameof(MemberNode.Other);
        }

        var after = NestedSetExpressions.QueryProperty(alias, property);

        // Assert
        Assert.IsAssignableFrom<MemberExpression>(before);
        Assert.IsAssignableFrom<MethodCallExpression>(after);
    }

    /// <summary>A custom expression observes only the alias check in addition to framework member construction.</summary>
    /// <param name="warm">Whether the exact mapped read has already been constructed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomExpressionTypeDoesNotAcquireAdditionalMetadataReads(bool warm)
    {
        // Arrange
        var (_, _, property) = RuntimeProperty();
        var actualAlias = new TypeObservationExpression();
        var referenceAlias = new TypeObservationExpression();

        if (warm)
        {
            _ = NestedSetExpressions.QueryProperty(Expression.Parameter(typeof(MemberNode)), property);
        }

        // Act
        var actual = NestedSetExpressions.QueryProperty(actualAlias, property);
        var reference = Expression.MakeMemberAccess(referenceAlias, property.PropertyInfo!);

        // Assert
        Assert.Same(actualAlias, Assert.IsAssignableFrom<MemberExpression>(actual).Expression);
        Assert.Same(referenceAlias, reference.Expression);
        // WHY: QueryProperty must test alias assignability once. An additional Type read would be
        // an observable behavior change for a custom expression, even when its result is RuntimeType.
        Assert.Equal(referenceAlias.TypeReads + 1, actualAlias.TypeReads);
    }

    /// <summary>Concurrent construction never retains another caller's query alias.</summary>
    [Fact]
    public void ConcurrentConstructionKeepsEveryAliasIndependent()
    {
        // Arrange
        var (_, _, property) = RuntimeProperty();
        var aliases = Enumerable.Range(0, 32)
            .Select(index => Expression.Parameter(typeof(MemberNode), $"node{index}"))
            .ToArray();

        var expressions = new Expression[aliases.Length];

        // Act
        Parallel.For(0, aliases.Length,
            index => expressions[index] = NestedSetExpressions.QueryProperty(aliases[index], property));

        // Assert
        Assert.All(Enumerable.Range(0, aliases.Length), index =>
            Assert.Same(aliases[index], Assert.IsAssignableFrom<MemberExpression>(expressions[index]).Expression));
    }

    /// <summary>Construction does not extend model or alias lifetime; a retained model is the positive control.</summary>
    /// <param name="retainModel">Whether the caller deliberately keeps the model strongly reachable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionDoesNotRootModelsOrQueryAliases(bool retainModel)
    {
        // Arrange
        var witnesses = PopulateWeakMembers(retainModel, out var retained);

        // Act
        CollectGarbage();

        // Assert
        Assert.Equal(retainModel, witnesses[0].IsAlive);
        Assert.Equal(retainModel, witnesses[1].IsAlive);
        Assert.Equal(retainModel, witnesses[2].IsAlive);
        Assert.False(witnesses[3].IsAlive);
        GC.KeepAlive(retained);
    }

    /// <summary>Warm mapped reads add no per-call metadata allocation beyond the required member expression.</summary>
    [Fact]
    public void MappedReadDoesNotAllocatePerCallMetadataState()
    {
        // Arrange
        var (_, _, property) = RuntimeProperty();
        var alias = Expression.Parameter(typeof(MemberNode), "node");
        _ = MeasureAllocations(alias, property, mapped: false);
        _ = MeasureAllocations(alias, property, mapped: true);

        // Act
        var direct = MeasureAllocations(alias, property, mapped: false);
        var mapped = MeasureAllocations(alias, property, mapped: true);

        // Assert
        Assert.True(direct > 0);
        Assert.InRange(mapped, 0, direct + 256);
    }

    /// <summary>Creates independent runtime metadata without an EF provider's global model cache.</summary>
    private static (RuntimeModel Model, RuntimeEntityType Entity, IProperty Property) RuntimeProperty()
    {
        var model = NewModel();
        var entity = model.AddEntityType("Mapped", typeof(MemberNode), sharedClrType: true);
        var property = entity.AddProperty(nameof(MemberNode.Value), typeof(int),
            propertyInfo: typeof(MemberNode).GetProperty(nameof(MemberNode.Value)));

        return (model, entity, property);
    }

    /// <summary>Obtains runtime metadata through normal EF initialization with independently owned services.</summary>
    private static RuntimeModel NewModel()
    {
        var options = new DbContextOptionsBuilder<ModelContext>().UseSqlite("Data Source=:memory:");
        var services = new ServiceCollection();

        // WHY: EF's global service-provider cache would retain the model and invalidate the weak-
        // ownership counterexample. An explicitly owned provider gives this model an independent lifetime.
        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        using var provider = services.BuildServiceProvider();
        using var context = new ModelContext(options.UseInternalServiceProvider(provider).Options);

        return Assert.IsType<RuntimeModel>(context.Model);
    }

    /// <summary>Builds the mapped read on detached runtime metadata before dropping its construction frames.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PopulateWeakMembers(bool retainModel, out IModel? retained)
    {
        var options = new DbContextOptionsBuilder<ModelContext>().UseSqlite("Data Source=:memory:");
        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        using var provider = services.BuildServiceProvider();
        using var context = new ModelContext(options.UseInternalServiceProvider(provider).Options);
        var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
        builder.SharedTypeEntity<MemberNode>("Mapped", mapping =>
        {
            mapping.HasNoKey();
            mapping.Property(node => node.Value);
        });

        // WHY: Pending DI resolver compilation can retain the provider and its cached context model.
        // Initialize this independent model through EF's public services without entering ModelSource's
        // cache, so that unrelated background work cannot become a root in this ownership counterexample.
        var model = Assert.IsType<RuntimeModel>(context.GetService<IModelRuntimeInitializer>()
            .Initialize(builder.FinalizeModel(), designTime: false));

        var entity = ((IModel)model).FindEntityType("Mapped")!;
        var property = entity.FindProperty(nameof(MemberNode.Value))!;

        var alias = Expression.Parameter(typeof(MemberNode), "node");
        _ = NestedSetExpressions.QueryProperty(alias, property);
        retained = retainModel ? model : null;

        return [new WeakReference(model), new WeakReference(entity), new WeakReference(property),
            new WeakReference(alias)];
    }

    /// <summary>Separates garbage collection from weak-target observation to avoid extending JIT local lifetimes.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    /// <summary>Measures only warm construction with preexisting alias and metadata objects.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureAllocations(ParameterExpression alias, IProperty property, bool mapped)
    {
        Expression? last = null;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1_024; index++)
        {
            last = mapped
                ? NestedSetExpressions.QueryProperty(alias, property)
                : Expression.MakeMemberAccess(alias, property.PropertyInfo!);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);

        return allocated;
    }

    /// <summary>Exposes metadata without ever permitting query construction to execute a CLR getter.</summary>
    private class MemberNode
    {
        /// <summary>Throws if a query builder accidentally reads the application instance.</summary>
        public int Value => throw new InvalidOperationException($"{GetType().Name}.Value must remain deferred.");

        /// <summary>Supplies an alternative member name for the mutable reflection counterexample.</summary>
        public int Other => throw new InvalidOperationException($"{GetType().Name}.Other must remain deferred.");

        /// <summary>Supplies a real field for changing the mutable model's storage mapping.</summary>
        public int Stored = -1;

    }

    /// <summary>Provides a compatible but distinct alias type for the same mapped CLR declaration.</summary>
    private sealed class DerivedMemberNode : MemberNode;

    /// <summary>Provides an incompatible alias with no relation to the mapped CLR declaration.</summary>
    private sealed class UnrelatedNode;

    /// <summary>Lets EF initialize an independent empty runtime model through its ordinary context services.</summary>
    private sealed class ModelContext(DbContextOptions<ModelContext> options) : DbContext(options);

    /// <summary>Exposes observable Type reads while behaving as a readable expression with an ordinary CLR type.</summary>
    private sealed class TypeObservationExpression : Expression
    {
        /// <summary>Gets the number of times query construction observed this expression's CLR type.</summary>
        internal int TypeReads { get; private set; }

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type
        {
            get
            {
                TypeReads++;

                return typeof(MemberNode);
            }
        }
    }

    /// <summary>Wraps metadata to prove that custom implementations retain their original per-call inspection.</summary>
    public class MetadataProbe : DispatchProxy
    {
        /// <summary>Gets or sets the immutable metadata delegated by this custom implementation.</summary>
        internal IProperty Target { get; set; } = null!;

        /// <summary>Gets the number of actual property-member inspections.</summary>
        internal int PropertyReads { get; private set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException("The proxy call requires metadata.");

            if (method.Name == "get_PropertyInfo")
            {
                PropertyReads++;
            }

            return method.Invoke(Target, BindingFlags.DoNotWrapExceptions, null, args, null);
        }
    }

    /// <summary>Changes assignability without changing the represented CLR type.</summary>
    private sealed class MutableDeclaringType(Type type) : TypeDelegator(type)
    {
        /// <summary>Gets or sets whether the next alias check accepts the represented type.</summary>
        internal bool AcceptsAlias { get; set; } = true;

        /// <inheritdoc />
        public override bool IsAssignableFrom(Type? candidate) => AcceptsAlias && base.IsAssignableFrom(candidate);
    }

    /// <summary>Preserves a real getter while deliberately exposing mutable reflection facts.</summary>
    private sealed class MutableProperty(PropertyInfo property, Type declaringType) : PropertyInfo
    {
        /// <summary>Gets or sets the name reported to EF's exact mapped-member lookup.</summary>
        internal string ReportedName { get; set; } = property.Name;

        /// <inheritdoc />
        public override PropertyAttributes Attributes => property.Attributes;

        /// <inheritdoc />
        public override bool CanRead => property.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => property.CanWrite;

        /// <inheritdoc />
        public override Type PropertyType => property.PropertyType;

        /// <inheritdoc />
        public override Type DeclaringType => declaringType;

        /// <inheritdoc />
        public override string Name => ReportedName;

        /// <inheritdoc />
        public override Type? ReflectedType => property.ReflectedType;

        /// <inheritdoc />
        public override MethodInfo[] GetAccessors(bool nonPublic) => property.GetAccessors(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetGetMethod(bool nonPublic) => property.GetGetMethod(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetSetMethod(bool nonPublic) => property.GetSetMethod(nonPublic);

        /// <inheritdoc />
        public override ParameterInfo[] GetIndexParameters() => property.GetIndexParameters();

        /// <inheritdoc />
        public override object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index,
            CultureInfo? culture) => property.GetValue(obj, invokeAttr, binder, index, culture);

        /// <inheritdoc />
        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, CultureInfo? culture) => property.SetValue(obj, value, invokeAttr, binder, index, culture);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(bool inherit) => property.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => property.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(Type attributeType, bool inherit) => property.IsDefined(attributeType, inherit);
    }
}
