﻿using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace ObjectComparison
{
    /// <summary>
    /// Configuration for object comparison with comprehensive options
    /// </summary>
    public class ComparisonConfig
    {
        /// <summary>
        /// Whether to compare private fields and properties
        /// </summary>
        public bool ComparePrivateFields { get; set; } = false;

        /// <summary>
        /// Whether to perform deep comparison of objects
        /// </summary>
        public bool DeepComparison { get; set; } = true;

        /// <summary>
        /// Number of decimal places to consider when comparing decimal values
        /// </summary>
        public int DecimalPrecision { get; set; } = 4;

        /// <summary>
        /// Properties to exclude from comparison
        /// </summary>
        public HashSet<string> ExcludedProperties { get; set; } = new();

        /// <summary>
        /// Custom comparers for specific types
        /// </summary>
        public Dictionary<Type, ICustomComparer> CustomComparers { get; set; } = new();

        /// <summary>
        /// Custom equality comparers for collection items of specific types
        /// </summary>
        public Dictionary<Type, IEqualityComparer> CollectionItemComparers { get; set; } = new();

        /// <summary>
        /// Whether to ignore the order of items in collections
        /// </summary>
        public bool IgnoreCollectionOrder { get; set; } = false;

        /// <summary>
        /// How to handle null values in reference types
        /// </summary>
        public NullHandling NullValueHandling { get; set; } = NullHandling.Strict;

        /// <summary>
        /// Maximum depth for comparison to prevent stack overflow
        /// </summary>
        public int MaxDepth { get; set; } = 100;

        /// <summary>
        /// Maximum number of objects to compare
        /// </summary>
        public int MaxObjectCount { get; set; } = 10000;

        /// <summary>
        /// Whether to use cached reflection metadata
        /// </summary>
        public bool UseCachedMetadata { get; set; } = true;

        /// <summary>
        /// Optional logger for diagnostics
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Whether to track property access paths for better error reporting
        /// </summary>
        public bool TrackPropertyPaths { get; set; } = true;

        /// <summary>
        /// Whether to compare read-only properties
        /// </summary>
        public bool CompareReadOnlyProperties { get; set; } = true;

        /// <summary>
        /// Relative tolerance for floating-point comparisons
        /// </summary>
        public double FloatingPointTolerance { get; set; } = 1e-10;

        /// <summary>
        /// Whether to use relative tolerance for floating-point comparisons
        /// </summary>
        public bool UseRelativeFloatingPointComparison { get; set; } = true;
    }

    /// <summary>
    /// Interface for custom comparison logic
    /// </summary>
    public interface ICustomComparer
    {
        bool AreEqual(object obj1, object obj2, ComparisonConfig config);
    }

    /// <summary>
    /// Detailed results of object comparison
    /// </summary>
    public class ComparisonResult
    {
        /// <summary>
        /// Whether the objects are considered equal
        /// </summary>
        public bool AreEqual { get; set; } = true;

        /// <summary>
        /// List of differences found during comparison
        /// </summary>
        public List<string> Differences { get; set; } = new();

        /// <summary>
        /// The path where comparison stopped (if max depth was reached)
        /// </summary>
        public string MaxDepthPath { get; set; }

        /// <summary>
        /// Time taken to perform the comparison
        /// </summary>
        public TimeSpan ComparisonTime { get; set; }

        /// <summary>
        /// Number of objects compared
        /// </summary>
        public int ObjectsCompared { get; set; }

        /// <summary>
        /// Number of properties compared
        /// </summary>
        public int PropertiesCompared { get; set; }

        /// <summary>
        /// Maximum depth reached during comparison
        /// </summary>
        public int MaxDepthReached { get; set; }

        /// <summary>
        /// Collection of property paths that were different
        /// </summary>
        public HashSet<string> DifferentPaths { get; } = new();
    }

    /// <summary>
    /// Exception thrown during comparison operations
    /// </summary>
    public class ComparisonException : Exception
    {
        public string Path { get; }

        public ComparisonException(string message) : base(message)
        {
        }

        public ComparisonException(string message, string path) : base(message)
        {
            Path = path;
        }

        public ComparisonException(string message, string path, Exception inner)
            : base(message, inner)
        {
            Path = path;
        }
    }

    /// <summary>
    /// Cache for type metadata and compiled expressions
    /// </summary>
    internal static class TypeCache
    {
        private static readonly ConcurrentDictionary<Type, TypeMetadata> MetadataCache = new();
        private static readonly ConcurrentDictionary<Type, Func<object, object>> CloneFuncs = new();
        private static readonly ConcurrentDictionary<(Type, string), Func<object, object>> PropertyGetters = new();
        private static readonly ConcurrentDictionary<(Type, string), Action<object, object>> PropertySetters = new();

        public static TypeMetadata GetMetadata(Type type, bool useCache)
        {
            if (!useCache)
            {
                return new TypeMetadata(type);
            }

            return MetadataCache.GetOrAdd(type, t => new TypeMetadata(t));
        }

        public static Func<object, object> GetCloneFunc(Type type)
        {
            return CloneFuncs.GetOrAdd(type, CreateCloneExpression);
        }

        public static Func<object, object> GetPropertyGetter(Type type, string propertyName)
        {
            return PropertyGetters.GetOrAdd((type, propertyName), key => CreatePropertyGetter(key.Item1, key.Item2));
        }

        public static Action<object, object> GetPropertySetter(Type type, string propertyName)
        {
            return PropertySetters.GetOrAdd((type, propertyName), key => CreatePropertySetter(key.Item1, key.Item2));
        }

        private static Func<object, object> CreateCloneExpression(Type type)
        {
            // Implementation will be shown in the cloning section
            throw new NotImplementedException();
        }

        private static Func<object, object> CreatePropertyGetter(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
            {
                throw new ArgumentException($"Property {propertyName} not found on type {type.Name}");
            }

            var parameter = Expression.Parameter(typeof(object), "obj");
            var convertedParameter = Expression.Convert(parameter, type);
            var propertyAccess = Expression.Property(convertedParameter, property);
            var convertedProperty = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object>>(convertedProperty, parameter).Compile();
        }

        private static Action<object, object> CreatePropertySetter(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
            {
                throw new ArgumentException($"Property {propertyName} not found on type {type.Name}");
            }

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var convertedInstance = Expression.Convert(instanceParam, type);
            var convertedValue = Expression.Convert(valueParam, property.PropertyType);
            var propertyAccess = Expression.Property(convertedInstance, property);
            var assign = Expression.Assign(propertyAccess, convertedValue);

            return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
        }
    }

    /// <summary>
    /// Metadata for a type including its properties, fields, and compiled accessors
    /// </summary>
    internal class TypeMetadata
    {
        public PropertyInfo[] Properties { get; }
        public FieldInfo[] Fields { get; }
        public bool IsSimpleType { get; }
        public Type UnderlyingType { get; }
        public bool HasCustomEquality { get; }
        public Func<object, object, bool> EqualityComparer { get; }
        public Type ItemType { get; }
        public bool IsCollection { get; }

        public TypeMetadata(Type type)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            Properties = type.GetProperties(flags);
            Fields = type.GetFields(flags);
            IsSimpleType = IsSimpleTypeInternal(type);
            UnderlyingType = Nullable.GetUnderlyingType(type);
            HasCustomEquality = typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type);

            if (HasCustomEquality)
            {
                EqualityComparer = CreateEqualityComparer(type);
            }

            IsCollection = typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
            if (IsCollection)
            {
                ItemType = GetCollectionItemType(type);
            }
        }

        private static bool IsSimpleTypeInternal(Type type)
        {
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
                type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
                type == typeof(TimeSpan) || type == typeof(Guid))
            {
                return true;
            }

            var typeCode = Type.GetTypeCode(type);
            return typeCode != TypeCode.Object;
        }

        private static Func<object, object, bool> CreateEqualityComparer(Type type)
        {
            var method = type.GetMethod("Equals", new[] { type });
            if (method == null) return null;

            var param1 = Expression.Parameter(typeof(object), "x");
            var param2 = Expression.Parameter(typeof(object), "y");
            var typed1 = Expression.Convert(param1, type);
            var typed2 = Expression.Convert(param2, type);
            var equalCall = Expression.Call(typed1, method, typed2);

            return Expression.Lambda<Func<object, object, bool>>(equalCall, param1, param2).Compile();
        }

        private static Type GetCollectionItemType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            var enumType = type.GetInterfaces()
                .Concat(new[] { type })
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumType?.GetGenericArguments()[0];
        }
    }

    /// <summary>
    /// Advanced cloning functionality using expression trees
    /// </summary>
    internal class ExpressionCloner
    {
        private readonly ComparisonConfig _config;
        private readonly HashSet<object> _clonedObjects = new();
        private readonly Dictionary<Type, Func<object, object>> _customCloners = new();

        public ExpressionCloner(ComparisonConfig config)
        {
            _config = config;
            InitializeCustomCloners();
        }

        private void InitializeCustomCloners()
        {
            // Add custom cloners for specific types
            _customCloners[typeof(DateTime)] = obj => ((DateTime)obj);
            _customCloners[typeof(string)] = obj => obj;
            _customCloners[typeof(decimal)] = obj => obj;
            _customCloners[typeof(Guid)] = obj => obj;
        }

        public T Clone<T>(T obj)
        {
            if (obj == null) return default;

            var type = obj.GetType();
            if (_customCloners.TryGetValue(type, out var customCloner))
            {
                return (T)customCloner(obj);
            }

            return (T)CloneObject(obj);
        }

        private object CloneObject(object obj)
        {
            if (obj == null) return null;

            var type = obj.GetType();
            var metadata = TypeCache.GetMetadata(type, _config.UseCachedMetadata);

            // Handle simple types
            if (metadata.IsSimpleType)
            {
                return obj;
            }

            // Check for circular references
            if (!_clonedObjects.Add(obj))
            {
                _config.Logger?.LogWarning("Circular reference detected while cloning type {Type}", type.Name);
                return obj;
            }

            try
            {
                // Handle collections
                if (metadata.IsCollection)
                {
                    return CloneCollection(obj, type, metadata);
                }

                // Handle complex objects
                return CloneComplexObject(obj, type, metadata);
            }
            finally
            {
                _clonedObjects.Remove(obj);
            }
        }

        private object CloneCollection(object obj, Type type, TypeMetadata metadata)
        {
            var enumerable = (IEnumerable)obj;

            // Handle arrays
            if (type.IsArray)
            {
                var array = (Array)obj;
                var elementType = type.GetElementType();
                var clone = Array.CreateInstance(elementType, array.Length);

                for (int i = 0; i < array.Length; i++)
                {
                    clone.SetValue(CloneObject(array.GetValue(i)), i);
                }

                return clone;
            }

            // Handle generic collections
            if (metadata.ItemType != null)
            {
                var listType = typeof(List<>).MakeGenericType(metadata.ItemType);
                var list = (IList)Activator.CreateInstance(listType);

                foreach (var item in enumerable)
                {
                    list.Add(CloneObject(item));
                }

                // If the original was a List<T>, return as is
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    return list;
                }

                // Try to convert to the original collection type
                try
                {
                    var constructor = type.GetConstructor(new[]
                        { typeof(IEnumerable<>).MakeGenericType(metadata.ItemType) });
                    if (constructor != null)
                    {
                        return constructor.Invoke(new[] { list });
                    }
                }
                catch (Exception ex)
                {
                    _config.Logger?.LogWarning(ex, "Failed to convert cloned collection to type {Type}", type.Name);
                }

                return list;
            }

            // Handle non-generic collections
            var nonGenericList = new ArrayList();
            foreach (var item in enumerable)
            {
                nonGenericList.Add(CloneObject(item));
            }

            return nonGenericList;
        }

        private object CloneComplexObject(object obj, Type type, TypeMetadata metadata)
        {
            // Create instance
            var clone = CreateInstance(type);
            if (clone == null) return null;

            // Clone properties
            foreach (var prop in metadata.Properties)
            {
                if (!prop.CanWrite) continue;
                if (_config.ExcludedProperties.Contains(prop.Name)) continue;

                try
                {
                    var getter = TypeCache.GetPropertyGetter(type, prop.Name);
                    var setter = TypeCache.GetPropertySetter(type, prop.Name);
                    var value = getter(obj);
                    var clonedValue = CloneObject(value);
                    setter(clone, clonedValue);
                }
                catch (Exception ex)
                {
                    _config.Logger?.LogWarning(ex, "Failed to clone property {Property} of type {Type}",
                        prop.Name, type.Name);
                }
            }

            // Clone fields
            if (_config.ComparePrivateFields)
            {
                foreach (var field in metadata.Fields)
                {
                    if (_config.ExcludedProperties.Contains(field.Name)) continue;

                    try
                    {
                        var value = field.GetValue(obj);
                        var clonedValue = CloneObject(value);
                        field.SetValue(clone, clonedValue);
                    }
                    catch (Exception ex)
                    {
                        _config.Logger?.LogWarning(ex, "Failed to clone field {Field} of type {Type}",
                            field.Name, type.Name);
                    }
                }
            }

            return clone;
        }

        private static object CreateInstance(Type type)
        {
            try
            {
                // Try to get the parameterless constructor
                var constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor != null)
                {
                    return Activator.CreateInstance(type);
                }

                // If no parameterless constructor exists, try to create uninitialized object
                return FormatterServices.GetUninitializedObject(type);
            }
            catch (Exception ex)
            {
                throw new ComparisonException($"Failed to create instance of type {type.Name}", "", ex);
            }
        }
    }

    /// <summary>
    /// Main object comparer class with optimized implementation
    /// </summary>
    public partial class ObjectComparer
    {
        private readonly ComparisonConfig _config;
        private readonly ExpressionCloner _cloner;

        public ObjectComparer(ComparisonConfig? config = null)
        {
            _config = config ?? new ComparisonConfig();
            _cloner = new ExpressionCloner(_config);
        }

        /// <summary>
        /// Takes a snapshot of an object for later comparison
        /// </summary>
        public T TakeSnapshot<T>(T obj)
        {
            return _cloner.Clone(obj);
        }

        /// <summary>
        /// Compares two objects and returns detailed results
        /// </summary>
        public ComparisonResult Compare<T>(T obj1, T obj2)
        {
            var context = new ComparisonContext();
            var result = new ComparisonResult();

            try
            {
                context.Timer.Start();
                CompareObjectsIterative(obj1, obj2, "", result, context);
            }
            catch (Exception ex)
            {
                throw new ComparisonException("Comparison failed", "", ex);
            }
            finally
            {
                context.Timer.Stop();
                result.ComparisonTime = context.Timer.Elapsed;
                result.ObjectsCompared = context.ObjectsCompared;
                result.MaxDepthReached = context.MaxDepthReached;
            }

            return result;
        }
    }

    /// <summary>
    /// Context for tracking comparison state
    /// </summary>
    internal class ComparisonContext
    {
        public HashSet<ComparisonPair> ComparedObjects { get; } = new();
        public int CurrentDepth { get; set; }
        public Stopwatch Timer { get; } = new();
        public int ObjectsCompared { get; set; }
        public int MaxDepthReached { get; set; }
        public readonly Stack<object> ObjectStack = new();

        public void PushObject(object obj)
        {
            ObjectStack.Push(obj);
            ObjectsCompared++;
            MaxDepthReached = Math.Max(MaxDepthReached, ObjectStack.Count);
        }

        public void PopObject()
        {
            if (ObjectStack.Count > 0)
            {
                ObjectStack.Pop();
            }
        }

        public readonly struct ComparisonPair : IEquatable<ComparisonPair>
        {
            private readonly object _obj1;
            private readonly object _obj2;
            private readonly int _hashCode;

            public ComparisonPair(object obj1, object obj2)
            {
                _obj1 = obj1;
                _obj2 = obj2;
                _hashCode = HashCode.Combine(
                    RuntimeHelpers.GetHashCode(obj1),
                    RuntimeHelpers.GetHashCode(obj2)
                );
            }

            public bool Equals(ComparisonPair other)
            {
                return ReferenceEquals(_obj1, other._obj1) &&
                       ReferenceEquals(_obj2, other._obj2);
            }

            public override bool Equals(object obj)
            {
                return obj is ComparisonPair other && Equals(other);
            }

            public override int GetHashCode() => _hashCode;
        }
    }

    public partial class ObjectComparer
    {
        private void CompareObjectsIterative(object obj1, object obj2, string path,
            ComparisonResult result, ComparisonContext context)
        {
            var stack = new Stack<(object, object, string, int)>();
            stack.Push((obj1, obj2, path, 0));

            while (stack.Count > 0 && context.ObjectsCompared < _config.MaxObjectCount)
            {
                var (current1, current2, currentPath, depth) = stack.Pop();
                context.PushObject(current1);

                try
                {
                    if (depth >= _config.MaxDepth)
                    {
                        result.MaxDepthPath = currentPath;
                        continue;
                    }

                    if (HandleNulls(current1, current2, currentPath, result))
                    {
                        continue;
                    }

                    var type = current1?.GetType() ?? current2?.GetType();
                    var metadata = TypeCache.GetMetadata(type, _config.UseCachedMetadata);

                    // Handle circular references
                    var pair = new ComparisonContext.ComparisonPair(current1, current2);
                    if (!context.ComparedObjects.Add(pair))
                    {
                        continue;
                    }

                    if (_config.CustomComparers.TryGetValue(type, out var customComparer))
                    {
                        HandleCustomComparison(customComparer, current1, current2, currentPath, result);
                        continue;
                    }

                    if (metadata.UnderlyingType != null)
                    {
                        CompareNullableTypes(current1, current2, currentPath, result, metadata);
                    }
                    else if (metadata.IsSimpleType)
                    {
                        CompareSimpleTypes(current1, current2, currentPath, result, metadata);
                    }
                    else if (metadata.IsCollection)
                    {
                        CompareCollections(current1, current2, currentPath, result, stack, depth, metadata);
                    }
                    else
                    {
                        CompareComplexObjects(current1, current2, currentPath, result, metadata, stack, depth);
                    }
                }
                finally
                {
                    context.PopObject();
                }
            }

            if (context.ObjectsCompared >= _config.MaxObjectCount)
            {
                result.Differences.Add(
                    $"Comparison aborted: exceeded maximum object count of {_config.MaxObjectCount}");
                result.AreEqual = false;
            }
        }

        private bool HandleNulls(object obj1, object obj2, string path, ComparisonResult result)
        {
            if (ReferenceEquals(obj1, obj2)) return true;

            if (obj1 == null || obj2 == null)
            {
                if (_config.NullValueHandling == NullHandling.Loose && IsEmpty(obj1) && IsEmpty(obj2))
                {
                    return true;
                }

                result.Differences.Add($"Null difference at {path}: one object is null while the other is not");
                result.AreEqual = false;
                return true;
            }

            return false;
        }

        private void HandleCustomComparison(ICustomComparer comparer, object obj1, object obj2,
            string path, ComparisonResult result)
        {
            try
            {
                if (!comparer.AreEqual(obj1, obj2, _config))
                {
                    result.Differences.Add($"Custom comparison failed at {path}");
                    result.AreEqual = false;
                }
            }
            catch (Exception ex)
            {
                throw new ComparisonException($"Custom comparison failed at {path}", path, ex);
            }
        }

        private void CompareNullableTypes(object obj1, object obj2, string path,
            ComparisonResult result, TypeMetadata metadata)
        {
            try
            {
                var value1 = obj1 != null ? TypeCache.GetPropertyGetter(obj1.GetType(), "Value")(obj1) : null;
                var value2 = obj2 != null ? TypeCache.GetPropertyGetter(obj2.GetType(), "Value")(obj2) : null;

                if (metadata.UnderlyingType == typeof(decimal))
                {
                    CompareDecimals(value1 as decimal?, value2 as decimal?, path, result);
                    return;
                }

                if (metadata.HasCustomEquality && metadata.EqualityComparer != null)
                {
                    if (!metadata.EqualityComparer(value1, value2))
                    {
                        result.Differences.Add($"Nullable value difference at {path}: {value1} != {value2}");
                        result.AreEqual = false;
                    }

                    return;
                }

                if (!Equals(value1, value2))
                {
                    result.Differences.Add($"Nullable value difference at {path}: {value1} != {value2}");
                    result.AreEqual = false;
                }
            }
            catch (Exception ex)
            {
                throw new ComparisonException($"Failed to compare nullable values at {path}", path, ex);
            }
        }

        private void CompareSimpleTypes(object obj1, object obj2, string path,
            ComparisonResult result, TypeMetadata metadata)
        {
            if (metadata.HasCustomEquality && metadata.EqualityComparer != null)
            {
                if (!metadata.EqualityComparer(obj1, obj2))
                {
                    result.Differences.Add($"Value difference at {path}: {obj1} != {obj2}");
                    result.AreEqual = false;
                }

                return;
            }

            if (obj1 is decimal dec1 && obj2 is decimal dec2)
            {
                CompareDecimals(dec1, dec2, path, result);
                return;
            }

            if (obj1 is float f1 && obj2 is float f2)
            {
                CompareFloats(f1, f2, path, result);
                return;
            }

            if (obj1 is double d1 && obj2 is double d2)
            {
                CompareDoubles(d1, d2, path, result);
                return;
            }

            if (!obj1.Equals(obj2))
            {
                result.Differences.Add($"Value difference at {path}: {obj1} != {obj2}");
                result.AreEqual = false;
            }
        }

        private void CompareDecimals(decimal? dec1, decimal? dec2, string path, ComparisonResult result)
        {
            var value1 = dec1 ?? 0m;
            var value2 = dec2 ?? 0m;
            var rounded1 = Math.Round(value1, _config.DecimalPrecision);
            var rounded2 = Math.Round(value2, _config.DecimalPrecision);

            if (rounded1 != rounded2)
            {
                result.Differences.Add($"Decimal difference at {path}: {rounded1} != {rounded2}");
                result.AreEqual = false;
            }
        }

        private void CompareFloats(float f1, float f2, string path, ComparisonResult result)
        {
            if (Math.Abs(f1 - f2) > float.Epsilon)
            {
                result.Differences.Add($"Float difference at {path}: {f1} != {f2}");
                result.AreEqual = false;
            }
        }

        private void CompareDoubles(double d1, double d2, string path, ComparisonResult result)
        {
            if (Math.Abs(d1 - d2) > double.Epsilon)
            {
                result.Differences.Add($"Double difference at {path}: {d1} != {d2}");
                result.AreEqual = false;
            }
        }

        private void CompareCollections(object obj1, object obj2, string path,
            ComparisonResult result, Stack<(object, object, string, int)> stack,
            int depth, TypeMetadata metadata)
        {
            try
            {
                var collection1 = (IEnumerable)obj1;
                var collection2 = (IEnumerable)obj2;

                var list1 = collection1?.Cast<object>().ToList() ?? new List<object>();
                var list2 = collection2?.Cast<object>().ToList() ?? new List<object>();

                if (list1.Count != list2.Count)
                {
                    result.Differences.Add($"Collection length difference at {path}: {list1.Count} != {list2.Count}");
                    result.AreEqual = false;
                    return;
                }

                // Check if we have a custom comparer for the collection items
                var hasCustomItemComparer = _config.CollectionItemComparers.TryGetValue(
                    metadata.ItemType ?? typeof(object),
                    out var itemComparer);

                if (_config.IgnoreCollectionOrder)
                {
                    if (hasCustomItemComparer)
                    {
                        CompareUnorderedCollectionsWithComparer(list1, list2, path, result, itemComparer);
                    }
                    else if (metadata.ItemType != null && IsSimpleType(metadata.ItemType))
                    {
                        CompareUnorderedCollectionsFast(list1, list2, path, result);
                    }
                    else
                    {
                        CompareUnorderedCollectionsSlow(list1, list2, path, result, stack, depth);
                    }
                }
                else
                {
                    if (hasCustomItemComparer)
                    {
                        CompareOrderedCollectionsWithComparer(list1, list2, path, result, itemComparer);
                    }
                    else
                    {
                        CompareOrderedCollections(list1, list2, path, result, stack, depth);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ComparisonException($"Failed to compare collections at {path}", path, ex);
            }
        }

        private void CompareUnorderedCollectionsWithComparer(List<object> list1, List<object> list2,
            string path, ComparisonResult result, IEqualityComparer itemComparer)
        {
            var unmatchedItems2 = new List<object>(list2);

            for (int i = 0; i < list1.Count; i++)
            {
                var item1 = list1[i];
                var matchFound = false;

                for (int j = unmatchedItems2.Count - 1; j >= 0; j--)
                {
                    if (itemComparer.Equals(item1, unmatchedItems2[j]))
                    {
                        unmatchedItems2.RemoveAt(j);
                        matchFound = true;
                        break;
                    }
                }

                if (!matchFound)
                {
                    result.Differences.Add($"No matching item found in collection at {path}[{i}]");
                    result.AreEqual = false;
                    return;
                }
            }
        }

        private void CompareUnorderedCollectionsFast(List<object> list1, List<object> list2,
            string path, ComparisonResult result)
        {
            var counts1 = new Dictionary<object, int>(new FastEqualityComparer());
            var counts2 = new Dictionary<object, int>(new FastEqualityComparer());

            foreach (var item in list1)
            {
                counts1.TryGetValue(item, out int count);
                counts1[item] = count + 1;
            }

            foreach (var item in list2)
            {
                counts2.TryGetValue(item, out int count);
                counts2[item] = count + 1;
            }

            foreach (var kvp in counts1)
            {
                if (!counts2.TryGetValue(kvp.Key, out int count2) || count2 != kvp.Value)
                {
                    result.Differences.Add($"Collection item count mismatch at {path}");
                    result.AreEqual = false;
                    return;
                }
            }
        }

        private void CompareUnorderedCollectionsSlow(List<object> list1, List<object> list2,
            string path, ComparisonResult result, Stack<(object, object, string, int)> stack, int depth)
        {
            var matched = new bool[list2.Count];

            for (int i = 0; i < list1.Count; i++)
            {
                var item1 = list1[i];
                var matchFound = false;

                for (int j = 0; j < list2.Count; j++)
                {
                    if (matched[j]) continue;

                    var tempResult = new ComparisonResult();
                    var tempStack = new Stack<(object, object, string, int)>();
                    tempStack.Push((item1, list2[j], $"{path}[{i}]", depth + 1));

                    while (tempStack.Count > 0)
                    {
                        var (tempObj1, tempObj2, tempPath, tempDepth) = tempStack.Pop();
                        CompareObjectsIterative(tempObj1, tempObj2, tempPath, tempResult, new ComparisonContext());
                    }

                    if (tempResult.AreEqual)
                    {
                        matched[j] = true;
                        matchFound = true;
                        break;
                    }
                }

                if (!matchFound)
                {
                    result.Differences.Add($"No matching item found in collection at {path}[{i}]");
                    result.AreEqual = false;
                    return;
                }
            }
        }

        private void CompareOrderedCollectionsWithComparer(List<object> list1, List<object> list2,
            string path, ComparisonResult result, IEqualityComparer itemComparer)
        {
            for (int i = 0; i < list1.Count; i++)
            {
                if (!itemComparer.Equals(list1[i], list2[i]))
                {
                    result.Differences.Add($"Collection items differ at {path}[{i}]");
                    result.AreEqual = false;
                    return;
                }
            }
        }

        private void CompareOrderedCollections(List<object> list1, List<object> list2,
            string path, ComparisonResult result, Stack<(object, object, string, int)> stack, int depth)
        {
            for (int i = 0; i < list1.Count; i++)
            {
                stack.Push((list1[i], list2[i], $"{path}[{i}]", depth + 1));
            }
        }

        private void CompareComplexObjects(object obj1, object obj2, string path,
            ComparisonResult result, TypeMetadata metadata, Stack<(object, object, string, int)> stack, int depth)
        {
            // Compare properties
            foreach (var prop in metadata.Properties)
            {
                if (!ShouldCompareProperty(prop)) continue;

                try
                {
                    var getter = TypeCache.GetPropertyGetter(obj1.GetType(), prop.Name);
                    var value1 = getter(obj1);
                    var value2 = getter(obj2);

                    if (_config.DeepComparison)
                    {
                        stack.Push((value1, value2, $"{path}.{prop.Name}", depth + 1));
                    }
                    else if (!AreValuesEqual(value1, value2))
                    {
                        result.Differences.Add($"Property difference at {path}.{prop.Name}");
                        result.AreEqual = false;
                    }
                }
                catch (Exception ex)
                {
                    _config.Logger?.LogWarning(ex, "Failed to compare property {Property} at {Path}", prop.Name, path);
                    throw new ComparisonException($"Error comparing property {prop.Name}", path, ex);
                }
            }

            // Compare fields if configured
            if (_config.ComparePrivateFields)
            {
                foreach (var field in metadata.Fields)
                {
                    if (_config.ExcludedProperties.Contains(field.Name)) continue;

                    try
                    {
                        var value1 = field.GetValue(obj1);
                        var value2 = field.GetValue(obj2);

                        if (_config.DeepComparison)
                        {
                            stack.Push((value1, value2, $"{path}.{field.Name}", depth + 1));
                        }
                        else if (!AreValuesEqual(value1, value2))
                        {
                            result.Differences.Add($"Field difference at {path}.{field.Name}");
                            result.AreEqual = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _config.Logger?.LogWarning(ex, "Failed to compare field {Field} at {Path}", field.Name, path);
                        throw new ComparisonException($"Error comparing field {field.Name}", path, ex);
                    }
                }
            }
        }

        private bool ShouldCompareProperty(PropertyInfo prop)
        {
            if (_config.ExcludedProperties.Contains(prop.Name)) return false;
            if (!prop.CanRead) return false;
            if (!_config.CompareReadOnlyProperties && !prop.CanWrite) return false;
            return true;
        }

        private bool AreValuesEqual(object value1, object value2)
        {
            if (ReferenceEquals(value1, value2)) return true;
            if (value1 == null || value2 == null) return false;
            return value1.Equals(value2);
        }

        private bool IsEmpty(object obj)
        {
            if (obj == null) return true;
            if (obj is string str) return string.IsNullOrEmpty(str);
            if (obj is IEnumerable enumerable) return !enumerable.Cast<object>().Any();
            return false;
        }

        private bool IsSimpleType(Type type)
        {
            return TypeCache.GetMetadata(type, _config.UseCachedMetadata).IsSimpleType;
        }

        private class FastEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.Equals(y);
            }

            public int GetHashCode(object obj)
            {
                return obj?.GetHashCode() ?? 0;
            }
        }
    }

    /// <summary>
    /// Specialized collection handling utilities
    /// </summary>
    internal static class CollectionHandling
    {
        public static object CloneCollection(Type collectionType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            // Handle arrays
            if (collectionType.IsArray)
            {
                return CloneArray(collectionType, source, elementCloner);
            }

            // Handle dictionaries
            if (IsDictionary(collectionType))
            {
                return CloneDictionary(collectionType, source, elementCloner);
            }

            // Handle sets
            if (IsSet(collectionType))
            {
                return CloneSet(collectionType, source, elementCloner);
            }

            // Handle queues and stacks
            if (IsQueueOrStack(collectionType))
            {
                return CloneQueueOrStack(collectionType, source, elementCloner);
            }

            // Default to List<T> for other collection types
            return CloneGenericList(collectionType, source, elementCloner);
        }

        private static object CloneArray(Type arrayType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            var elementType = arrayType.GetElementType();
            var sourceArray = source.Cast<object>().ToArray();
            var array = Array.CreateInstance(elementType, sourceArray.Length);

            for (int i = 0; i < sourceArray.Length; i++)
            {
                array.SetValue(elementCloner(sourceArray[i]), i);
            }

            return array;
        }

        private static object CloneDictionary(Type dictType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            var genericArgs = dictType.GetGenericArguments();
            var keyType = genericArgs[0];
            var valueType = genericArgs[1];

            var dictType1 = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            var dict = Activator.CreateInstance(dictType1);
            var addMethod = dictType1.GetMethod("Add");

            foreach (dynamic entry in source)
            {
                var clonedKey = elementCloner(entry.Key);
                var clonedValue = elementCloner(entry.Value);
                addMethod.Invoke(dict, new[] { clonedKey, clonedValue });
            }

            return dict;
        }

        private static object CloneSet(Type setType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            var elementType = setType.GetGenericArguments()[0];
            var hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
            var set = Activator.CreateInstance(hashSetType);
            var addMethod = hashSetType.GetMethod("Add");

            foreach (var item in source)
            {
                var clonedItem = elementCloner(item);
                addMethod.Invoke(set, new[] { clonedItem });
            }

            return set;
        }

        private static object CloneQueueOrStack(Type collectionType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            var elementType = collectionType.GetGenericArguments()[0];
            var items = source.Cast<object>().Select(elementCloner).ToList();

            if (collectionType.GetGenericTypeDefinition() == typeof(Queue<>))
            {
                var queueType = typeof(Queue<>).MakeGenericType(elementType);
                var queue = Activator.CreateInstance(queueType);
                var enqueueMethod = queueType.GetMethod("Enqueue");

                foreach (var item in items)
                {
                    enqueueMethod.Invoke(queue, new[] { item });
                }

                return queue;
            }
            else // Stack<T>
            {
                var stackType = typeof(Stack<>).MakeGenericType(elementType);
                var stack = Activator.CreateInstance(stackType);
                var pushMethod = stackType.GetMethod("Push");

                // Push in reverse order to maintain original order
                foreach (var item in items.AsEnumerable().Reverse())
                {
                    pushMethod.Invoke(stack, new[] { item });
                }

                return stack;
            }
        }

        private static object CloneGenericList(Type collectionType, IEnumerable source,
            Func<object, object> elementCloner)
        {
            var elementType = GetElementType(collectionType);
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");

            foreach (var item in source)
            {
                var clonedItem = elementCloner(item);
                addMethod.Invoke(list, new[] { clonedItem });
            }

            // If the original type was a List<T>, return as is
            if (IsGenericList(collectionType))
            {
                return list;
            }

            // Try to convert to the original collection type
            try
            {
                var constructor = collectionType.GetConstructor(
                    new[] { typeof(IEnumerable<>).MakeGenericType(elementType) });

                if (constructor != null)
                {
                    return constructor.Invoke(new[] { list });
                }
            }
            catch
            {
                // Fall back to list if conversion fails
            }

            return list;
        }

        private static bool IsDictionary(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                    type.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        }

        private static bool IsSet(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(HashSet<>) ||
                    type.GetGenericTypeDefinition() == typeof(ISet<>));
        }

        private static bool IsQueueOrStack(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(Queue<>) ||
                    type.GetGenericTypeDefinition() == typeof(Stack<>));
        }

        private static bool IsGenericList(Type type)
        {
            return type.IsGenericType &&
                   type.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static Type GetElementType(Type collectionType)
        {
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            if (collectionType.IsGenericType)
            {
                var genericArgs = collectionType.GetGenericArguments();
                if (genericArgs.Length == 1)
                {
                    return genericArgs[0];
                }
            }

            var enumType = collectionType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumType?.GetGenericArguments()[0] ?? typeof(object);
        }
    }

    /// <summary>
    /// Improved numeric comparison utilities
    /// </summary>
    internal static class NumericComparison
    {
        public static bool AreFloatingPointEqual(double value1, double value2, ComparisonConfig config)
        {
            if (double.IsNaN(value1) && double.IsNaN(value2))
                return true;

            if (double.IsInfinity(value1) || double.IsInfinity(value2))
                return value1 == value2;

            if (config.UseRelativeFloatingPointComparison)
            {
                return AreRelativelyEqual(value1, value2, config.FloatingPointTolerance);
            }

            return Math.Abs(value1 - value2) <= config.FloatingPointTolerance;
        }

        public static bool AreFloatingPointEqual(float value1, float value2, ComparisonConfig config)
        {
            if (float.IsNaN(value1) && float.IsNaN(value2))
                return true;

            if (float.IsInfinity(value1) || float.IsInfinity(value2))
                return value1 == value2;

            if (config.UseRelativeFloatingPointComparison)
            {
                return AreRelativelyEqual(value1, value2, (float)config.FloatingPointTolerance);
            }

            return Math.Abs(value1 - value2) <= config.FloatingPointTolerance;
        }

        private static bool AreRelativelyEqual(double value1, double value2, double relativeTolerance)
        {
            if (value1 == value2)
                return true;

            var absoluteDifference = Math.Abs(value1 - value2);
            var maxValue = Math.Max(Math.Abs(value1), Math.Abs(value2));

            if (maxValue < double.Epsilon)
                return absoluteDifference < double.Epsilon;

            return absoluteDifference / maxValue <= relativeTolerance;
        }

        private static bool AreRelativelyEqual(float value1, float value2, float relativeTolerance)
        {
            if (value1 == value2)
                return true;

            var absoluteDifference = Math.Abs(value1 - value2);
            var maxValue = Math.Max(Math.Abs(value1), Math.Abs(value2));

            if (maxValue < float.Epsilon)
                return absoluteDifference < float.Epsilon;

            return absoluteDifference / maxValue <= relativeTolerance;
        }
    }

    /// <summary>
    /// Thread-safe cache management
    /// </summary>
    internal static class ThreadSafeCache
    {
        private static readonly ConcurrentDictionary<Type, TypeMetadata> MetadataCache = new();
        private static readonly ConcurrentDictionary<(Type, string), Func<object, object>> PropertyGetters = new();

        private static readonly ConcurrentDictionary<Type, Func<object, IDictionary<string, object>>> DynamicAccessors =
            new();

        private static readonly ReaderWriterLockSlim CacheLock = new();
        private static readonly int MaxCacheSize = 1000; // Configurable cache size limit

        public static void ClearCaches()
        {
            using (new WriteLockScope(CacheLock))
            {
                MetadataCache.Clear();
                PropertyGetters.Clear();
                DynamicAccessors.Clear();
            }
        }

        public static TypeMetadata GetOrAddMetadata(Type type, Func<Type, TypeMetadata> factory)
        {
            if (MetadataCache.Count >= MaxCacheSize)
            {
                // Implement cache cleanup if needed
                TrimCache();
            }

            return MetadataCache.GetOrAdd(type, factory);
        }

        private static void TrimCache()
        {
            using (new WriteLockScope(CacheLock))
            {
                // Remove least recently used items
                var itemsToRemove = MetadataCache.Count - (MaxCacheSize * 3 / 4);
                if (itemsToRemove > 0)
                {
                    var oldest = MetadataCache.Take(itemsToRemove).ToList();
                    foreach (var item in oldest)
                    {
                        MetadataCache.TryRemove(item.Key, out _);
                    }
                }
            }
        }

        private class WriteLockScope : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;

            public WriteLockScope(ReaderWriterLockSlim @lock)
            {
                _lock = @lock;
                _lock.EnterWriteLock();
            }

            public void Dispose()
            {
                _lock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Dynamic object handling support
    /// </summary>
    internal class DynamicObjectComparer
    {
        private readonly ComparisonConfig _config;
        private readonly ConcurrentDictionary<Type, IDynamicTypeHandler> _typeHandlers;

        public DynamicObjectComparer(ComparisonConfig config)
        {
            _config = config;
            _typeHandlers = new ConcurrentDictionary<Type, IDynamicTypeHandler>();
            InitializeTypeHandlers();
        }

        private void InitializeTypeHandlers()
        {
            _typeHandlers[typeof(ExpandoObject)] = new ExpandoObjectHandler();
            _typeHandlers[typeof(DynamicObject)] = new DynamicObjectHandler();
            // Add other dynamic type handlers as needed
        }

        public bool AreEqual(object obj1, object obj2, string path, ComparisonResult result)
        {
            var type = obj1?.GetType() ?? obj2?.GetType();
            var handler = GetTypeHandler(type);

            if (handler == null)
            {
                result.Differences.Add($"Unsupported dynamic type at {path}: {type?.Name}");
                return false;
            }

            return handler.Compare(obj1, obj2, path, result, _config);
        }

        private IDynamicTypeHandler GetTypeHandler(Type type)
        {
            if (type == null) return null;

            var (handler, _) = _typeHandlers.GetOrAddWithStatus(type, t =>
            {
                if (typeof(ExpandoObject).IsAssignableFrom(t))
                    return new ExpandoObjectHandler();
                if (typeof(DynamicObject).IsAssignableFrom(t))
                    return new DynamicObjectHandler();
                // Add other type handler mappings
                return null;
            });

            return handler;
        }
    }

    internal interface IDynamicTypeHandler
    {
        bool Compare(object obj1, object obj2, string path, ComparisonResult result, ComparisonConfig config);
    }

    internal class ExpandoObjectHandler : IDynamicTypeHandler
    {
        public bool Compare(object obj1, object obj2, string path, ComparisonResult result, ComparisonConfig config)
        {
            var dict1 = obj1 as IDictionary<string, object>;
            var dict2 = obj2 as IDictionary<string, object>;

            if (dict1 == null || dict2 == null)
                return false;

            var allKeys = dict1.Keys.Union(dict2.Keys).Distinct();
            var isEqual = true;

            foreach (var key in allKeys)
            {
                var hasValue1 = dict1.TryGetValue(key, out var value1);
                var hasValue2 = dict2.TryGetValue(key, out var value2);

                if (!hasValue1 || !hasValue2)
                {
                    result.Differences.Add($"Property '{key}' exists in only one object at {path}");
                    isEqual = false;
                    continue;
                }

                if (value1 == null && value2 == null)
                    continue;

                if ((value1 == null) != (value2 == null))
                {
                    result.Differences.Add($"Property '{key}' null mismatch at {path}");
                    isEqual = false;
                    continue;
                }

                // Handle nested dynamic objects
                if (value1 is ExpandoObject)
                {
                    var nestedResult = new ComparisonResult();
                    if (!Compare(value1, value2, $"{path}.{key}", nestedResult, config))
                    {
                        result.Differences.AddRange(nestedResult.Differences);
                        isEqual = false;
                    }
                }
                else
                {
                    // Use the standard comparison logic for non-dynamic values
                    if (!AreValuesEqual(value1, value2, config))
                    {
                        result.Differences.Add($"Property '{key}' value mismatch at {path}");
                        isEqual = false;
                    }
                }
            }

            return isEqual;
        }

        private bool AreValuesEqual(object value1, object value2, ComparisonConfig config)
        {
            // Implement value comparison logic or delegate to main comparer
            // This is a simplified version
            return Equals(value1, value2);
        }
    }

    internal class DynamicObjectHandler : IDynamicTypeHandler
    {
        public bool Compare(object obj1, object obj2, string path, ComparisonResult result, ComparisonConfig config)
        {
            var dynamicObj1 = obj1 as DynamicObject;
            var dynamicObj2 = obj2 as DynamicObject;

            if (dynamicObj1 == null || dynamicObj2 == null)
                return false;

            var memberNames = GetMemberNames(dynamicObj1).Union(GetMemberNames(dynamicObj2)).Distinct();
            var isEqual = true;

            foreach (var memberName in memberNames)
            {
                var value1 = GetMemberValue(dynamicObj1, memberName);
                var value2 = GetMemberValue(dynamicObj2, memberName);

                if (!AreValuesEqual(value1, value2, $"{path}.{memberName}", result, config))
                {
                    isEqual = false;
                }
            }

            return isEqual;
        }

        private IEnumerable<string> GetMemberNames(DynamicObject obj)
        {
            var memberNames = new List<string>();
            obj.GetDynamicMemberNames()?.ToList().ForEach(name => memberNames.Add(name));
            return memberNames;
        }

        private object GetMemberValue(DynamicObject obj, string memberName)
        {
            var binder = new CustomGetMemberBinder(memberName);
            obj.TryGetMember(binder, out var result);
            return result;
        }

        private bool AreValuesEqual(object value1, object value2, string path,
            ComparisonResult result, ComparisonConfig config)
        {
            // Handle nested dynamic objects
            if (value1 is DynamicObject || value2 is DynamicObject)
            {
                return Compare(value1, value2, path, result, config);
            }

            // Handle ExpandoObjects
            if (value1 is ExpandoObject || value2 is ExpandoObject)
            {
                var handler = new ExpandoObjectHandler();
                return handler.Compare(value1, value2, path, result, config);
            }

            // Handle regular values
            if (!Equals(value1, value2))
            {
                result.Differences.Add($"Value mismatch at {path}: {value1} != {value2}");
                return false;
            }

            return true;
        }
    }

    internal class CustomGetMemberBinder : GetMemberBinder
    {
        public CustomGetMemberBinder(string name) : base(name, true)
        {
        }

        public override DynamicMetaObject FallbackGetMember(DynamicMetaObject target, DynamicMetaObject errorSuggestion)
        {
            throw new NotImplementedException();
        }
    }

    internal static class ThreadSafeExtensions
    {
        public static (TValue Value, bool Added) GetOrAddWithStatus<TKey, TValue>(
            this ConcurrentDictionary<TKey, TValue> dictionary,
            TKey key,
            Func<TKey, TValue> valueFactory)
        {
            bool added = false;
            var value = dictionary.GetOrAdd(key, k =>
            {
                added = true;
                return valueFactory(k);
            });
            return (value, added);
        }

        public static void AddRange<T>(this ConcurrentBag<T> bag, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                bag.Add(item);
            }
        }
    }

    /// <summary>
    /// Enum defining how null values should be handled
    /// </summary>
    public enum NullHandling
    {
        /// <summary>
        /// Treat null values as distinct values
        /// </summary>
        Strict,

        /// <summary>
        /// Treat null and empty values as equivalent
        /// </summary>
        Loose
    }
}