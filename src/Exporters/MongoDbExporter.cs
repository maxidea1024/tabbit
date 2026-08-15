using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using Tabbit.Models;
using Tabbit.Recipe;
using Serilog;

using ValueType = Tabbit.Models.ValueType;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// MongoDB target. One collection per table, one document per row.
/// </summary>
public class MongoDbRecipe : DatabaseRecipe
{
}

/// <summary>
/// Loads the cooked tables into MongoDB, one collection per table.
///
/// Rows go into a shadow collection which is then renamed over the live one.
/// Multi-document transactions would be an alternative but they need a replica set,
/// and renameCollection works on a standalone server too - so the atomic swap does
/// not depend on how the deployment is configured.
/// </summary>
[TabbitTarget("mongodb", TargetKind.Export, Order = 50)]
public class MongoDbExporter : DatabaseExporterBase<MongoDbRecipe>
{
    protected override string TargetName => "MongoDB";

    private const int InsertBatchRows = 1000;


    protected override void ExportTo(DatabaseRecipe recipe, Model model)
    {
        string connectionString = ConnectionString.Resolve(recipe.ConnectionString, RecipeSection);

        Log.Debug($"Connecting to MongoDB `{ConnectionString.RedactUri(connectionString)}`");

        var url = new MongoUrl(connectionString);
        if (string.IsNullOrEmpty(url.DatabaseName))
        {
            throw new TabbitException(
                $"Recipe section `{RecipeSection}` connection string must name a database, " +
                $"as in `mongodb://host:27017/mygame`.");
        }

        var client = new MongoClient(url);
        var database = client.GetDatabase(url.DatabaseName);

        try
        {
            foreach (var table in model.Tables)
            {
                string name = StorageName(recipe, table);

                LoadShadowCollection(database, name + ShadowSuffix, table);
            }

            foreach (var table in model.Tables)
                SwapIn(database, StorageName(recipe, table));
        }
        catch
        {
            DropShadowCollections(database, model, recipe);
            throw;
        }
    }

    private void LoadShadowCollection(IMongoDatabase database, string shadow, Table table)
    {
        database.DropCollection(shadow);

        var collection = database.GetCollection<BsonDocument>(shadow);
        var columns = Columns(table);

        var documents = new List<BsonDocument>(Math.Min(table.Data.Count, InsertBatchRows));

        foreach (var row in table.Data)
        {
            var document = new BsonDocument();

            foreach (var sf in columns)
                document.Add(ColumnName(sf), ToBsonValue(sf, row));

            // The primary index doubles as the document _id, so a lookup by index
            // uses the identity index Mongo maintains anyway rather than a second one.
            var indexColumn = columns.FirstOrDefault(sf => sf.IsIndexer);
            if (indexColumn is not null)
                document["_id"] = document[ColumnName(indexColumn)];

            documents.Add(document);

            if (documents.Count >= InsertBatchRows)
            {
                collection.InsertMany(documents);
                documents.Clear();
            }
        }

        if (documents.Count > 0)
            collection.InsertMany(documents);
    }

    private BsonValue ToBsonValue(SerialField sf, List<Cell> row)
    {
        if (sf.IsVariableLengthArray)
            return ToBsonArray((Array)row[sf.FirstField!.Index].Value!, ElementTypeOf(sf));

        if (sf.IsArray)
        {
            var array = new BsonArray();
            foreach (var field in sf.Fields)
                array.Add(ToBsonScalar(row[field.Index].Value!, ElementTypeOf(sf)));

            return array;
        }

        return ToBsonScalar(row[sf.FirstField!.Index].Value!, ElementTypeOf(sf));
    }

    private BsonValue ToBsonArray(Array elements, ValueType elementType)
    {
        var array = new BsonArray();

        if (elements is not null)
        {
            foreach (var element in elements)
                array.Add(ToBsonScalar(element, elementType));
        }

        return array;
    }

    private BsonValue ToBsonScalar(object? value, ValueType elementType)
    {
        switch (elementType)
        {
            case ValueType.String: return new BsonString((string?)value ?? "");
            case ValueType.Bool: return new BsonBoolean((bool)value!);
            case ValueType.Int32: return new BsonInt32((int)value!);
            case ValueType.Int64: return new BsonInt64((long)value!);

            // BSON has one floating point width, so a float widens to double.
            case ValueType.Float: return new BsonDouble((float)value!);
            case ValueType.Double: return new BsonDouble((double)value!);

            case ValueType.DateTime: return new BsonDateTime((DateTime)value!);

            // Ticks, not a BSON date: a TimeSpan is a duration and BsonDateTime
            // would state a point in time it does not have.
            case ValueType.TimeSpan: return new BsonInt64(((TimeSpan)value!).Ticks);

            // Stored as the canonical text form. A BSON binary subtype 4 would be
            // more compact, but every driver and shell renders the string readably.
            case ValueType.Uuid: return new BsonString(((Guid)value!).ToString());

            case ValueType.Enum:
            case ValueType.ForeignRecord:
                return new BsonInt32((int)value!);

            default:
                throw new TabbitException(
                    $"MongoDB exporter cannot map element type `{elementType}`.");
        }
    }

    /// <summary>
    /// Renames the shadow collection over the live one, dropping whatever was there.
    /// </summary>
    private void SwapIn(IMongoDatabase database, string name)
    {
        database.RenameCollection(name + ShadowSuffix, name,
            new RenameCollectionOptions { DropTarget = true });

        Log.Debug($"Swapped MongoDB collection `{name}` into place");
    }

    private void DropShadowCollections(IMongoDatabase database, Model model,
                                       DatabaseRecipe recipe)
    {
        // Best effort: the load already failed and that exception is the one worth
        // reporting.
        try
        {
            foreach (var table in model.Tables)
                database.DropCollection(StorageName(recipe, table) + ShadowSuffix);
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not clean up MongoDB shadow collections: {ex.Message}");
        }
    }
}
