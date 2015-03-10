using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkDynamicQueries
{
    public static class SelectiveQuery
    {
        public static ICollection<T> SelectProperties<T>(
            this IQueryable<T> source,
            IEnumerable<string> selectedProperties) where T : class
        {
            // Take properties from the mapped entitiy that match selected properties
            IDictionary<string, PropertyInfo> sourceProperties =
                GetTypeProperties<T>(selectedProperties);

            // Construct runtime type by given property configuration
            Type runtimeType = RuntimeTypeBuilder.GetRuntimeType(sourceProperties);
            Type sourceType = typeof(T);

            // Create instance of source parameter
            ParameterExpression sourceParameter = Expression.Parameter(sourceType, "t");

            // Take fields from generated runtime type
            FieldInfo[] runtimeTypeFields = runtimeType.GetFields();

            // Generate bindings from source type to runtime type
            IEnumerable<MemberBinding> bindingsToRuntimeType = runtimeTypeFields
                .Select(field => Expression.Bind(
                    field,
                    Expression.Property(
                        sourceParameter,
                        sourceProperties[field.Name]
                    )
                ));

            // Generate projection trom T to runtimeType and cast as IQueryable<object>
            IQueryable<object> runtimeTypeSelectExpressionQuery
                = GetTypeSelectExpressionQuery<object>(
                    sourceType,
                    runtimeType,
                    bindingsToRuntimeType,
                    source,
                    sourceParameter
            );

            // Get result from database
            List<object> listOfObjects = runtimeTypeSelectExpressionQuery.ToList();

            MethodInfo castMethod = typeof(Queryable)
                .GetMethod("Cast", BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(runtimeType);

            // Cast list<objects> to IQueryable<runtimeType>
            IQueryable castedSource = castMethod.Invoke(
                null,
                new Object[] { listOfObjects.AsQueryable() }
            ) as IQueryable;

            // Create instance of runtime type parameter
            ParameterExpression runtimeParameter = Expression.Parameter(runtimeType, "p");

            IDictionary<string, FieldInfo> dynamicTypeFieldsDict =
                runtimeTypeFields.ToDictionary(f => f.Name, f => f);

            // Generate bindings from runtime type to source type
            IEnumerable<MemberBinding> bindingsToTargetType = sourceProperties.Values
                .Select(property => Expression.Bind(
                    property,
                    Expression.Field(
                        runtimeParameter,
                        dynamicTypeFieldsDict[property.Name]
                    )
                ));

            // Generate projection trom runtimeType to T and cast as IQueryable<object>
            IQueryable<T> targetTypeSelectExpressionQuery
                = GetTypeSelectExpressionQuery<T>(
                    runtimeType,
                    sourceType,
                    bindingsToTargetType,
                    castedSource,
                    runtimeParameter
            );

            // Return list of T
            return targetTypeSelectExpressionQuery.ToList();
        }

        private static IQueryable<TT> GetTypeSelectExpressionQuery<TT>(
            Type sourceType,
            Type targetType,
            IEnumerable<MemberBinding> binding,
            IQueryable source,
            ParameterExpression sourceParameter)
        {
            LambdaExpression typeSelector =
                Expression.Lambda(
                    Expression.MemberInit(
                        Expression.New(
                            targetType.GetConstructor(Type.EmptyTypes)
                        ),
                        binding
                    ),
                    sourceParameter
                );

            MethodCallExpression typeSelectExpression =
                Expression.Call(
                    typeof(Queryable),
                    "Select",
                    new[] { sourceType, targetType },
                    Expression.Constant(source),
                    typeSelector
                );

            return Expression.Lambda(typeSelectExpression)
                .Compile()
                .DynamicInvoke() as IQueryable<TT>;
        }

        private static IDictionary<string, PropertyInfo> GetTypeProperties<T>(
            IEnumerable<string> selectedProperties) where T : class
        {
            var existedProperties = typeof(T)
                .GetProperties()
                .ToDictionary(p => p.Name);

            return selectedProperties
                .Where(existedProperties.ContainsKey)
                .ToDictionary(p => p, p => existedProperties[p]);
        }
    }
}