using System.Linq.Expressions;
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Infrastructure.Services;

public static class HawalaIdentifierQuery
{
    public static IQueryable<T> WhereIdentifier<T>(this IQueryable<T> query, HawalaIdentifierSearch? identifier,
        Expression<Func<T, long>> number, Expression<Func<T, string?>> reference)
    {
        if (identifier?.IsActive != true) return query;
        var search = HawalaIdentifierSearch.Create(identifier.Kind, identifier.Value);
        var selector = search.Kind == "Serial" ? (LambdaExpression)number : reference;
        var serial = search.Serial.GetValueOrDefault();
        var referenceValue = search.Value;
        Expression value = search.Kind == "Serial"
            ? ((Expression<Func<long>>)(() => serial)).Body
            : ((Expression<Func<string>>)(() => referenceValue)).Body;
        return query.Where(Expression.Lambda<Func<T, bool>>(Expression.Equal(selector.Body, value), selector.Parameters));
    }
}
