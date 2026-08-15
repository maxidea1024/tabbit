using Tabbit.Rules;
using Tabbit.Validation;

// A global rule reporting against a column, so the suite can check the other half of the
// reverse lookup: a report about the schema points at the header cell the column was declared
// in rather than at any row.

internal static class SchemaRules
{
    public static void Validate(IGlobalContext context)
    {
        var price = context.Schema.Table("Item").Field("Price");

        context.Error(price, $"This fixture rule always fails. `{price}` is a {price.TypeName}.");
    }
}
