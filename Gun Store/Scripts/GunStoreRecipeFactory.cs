#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class GunStoreRecipeFactory
{
    public static ScriptableObject?[] CreateAllRecipes()
    {
        return new[]
        {
            CreateAk47Recipe(),
            CreateBerettaM9Recipe(),
            CreateWinCheaterSxpRecipe(),
            CreateRpgRecipe(),
            CreateAmmoSmallRecipe(),
            CreateAmmoLargeRecipe()
        };
    }

    public static ScriptableObject? CreateAk47Recipe()
    {
        return CreateRecipe(
            "Ak47Recipe",
            "sSoU0AdCKUWnH+qY0k+K+A==",
            new[]
            {
                new RecipeIngredient("ba:itemname_plastic", 50),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 40),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartsexpensive", 20)
            },
            new RecipeIngredient("gunstore-businesstype:itemname_ak47", 20),
            new[]
            {
                new MachineVisualDefinition(
                    "ba:itemname_lasercuttingmachine",
                    "gunstore-businesstype:itemname_gunpartscheap",
                    "gunstore-businesstype:itemname_gunpartscheap"),
                new MachineVisualDefinition(
                    "ba:itemname_consumergoodsassemblymachine",
                    string.Empty,
                    "gunstore-businesstype:itemname_ak47")
            });
    }

    public static ScriptableObject? CreateAmmoSmallRecipe()
    {
        return CreateRecipe(
            "AmmoSmallRecipe",
            "EPrYqAvTRk2EUYJ8YjI1ow==",
            new[]
            {
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 20)
            },
            new RecipeIngredient("gunstore-businesstype:itemname_ammosmall", 60),
            new[]
            {
                new MachineVisualDefinition(
                    "ba:itemname_consumergoodsassemblymachine",
                    string.Empty,
                    "gunstore-businesstype:itemname_ammosmall")
            });
    }

    public static ScriptableObject? CreateAmmoLargeRecipe()
    {
        return CreateRecipe(
            "AmmoLargeRecipe",
            "Uf4L4mV0l0a0b8R8Yq+QpA==",
            new[]
            {
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 15)
            },
            new RecipeIngredient("gunstore-businesstype:itemname_ammolarge", 30),
            new[]
            {
                new MachineVisualDefinition(
                    "ba:itemname_consumergoodsassemblymachine",
                    string.Empty,
                    "gunstore-businesstype:itemname_ammolarge")
            });
    }

    public static ScriptableObject? CreateBerettaM9Recipe()
    {
        return CreateRecipe(
            "BerettaM9Recipe",
            "q8r9zW8x9UuP1VfZKx7xkA==",
            new[]
            {
                new RecipeIngredient("ba:itemname_plastic", 20),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 30),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartsexpensive", 10)
            },
            new RecipeIngredient("gunstore-businesstype:itemname_berettam9", 20),
            CreateGunRecipeMachineVisuals("gunstore-businesstype:itemname_berettam9"));
    }

    public static ScriptableObject? CreateWinCheaterSxpRecipe()
    {
        return CreateRecipe(
            "WinCheaterSxpRecipe",
            "4Q7GXs5h0USJ9V2q1tLh8w==",
            new[]
            {
                new RecipeIngredient("ba:itemname_plastic", 20),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 30),
            },
            new RecipeIngredient("gunstore-businesstype:itemname_wincheatersxp", 20),
            CreateGunRecipeMachineVisuals("gunstore-businesstype:itemname_wincheatersxp"));
    }

    public static ScriptableObject? CreateRpgRecipe()
    {
        return CreateRecipe(
            "RpgRecipe",
            "2T0wU6h4qkWJ4D9Nwq7G2A==",
            new[]
            {
                new RecipeIngredient("ba:itemname_plastic", 40),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartscheap", 40),
                new RecipeIngredient("gunstore-businesstype:itemname_gunpartsexpensive", 40)
            },
            new RecipeIngredient("gunstore-businesstype:itemname_rpg", 10),
            CreateGunRecipeMachineVisuals("gunstore-businesstype:itemname_rpg"));
    }

    private static ScriptableObject? CreateRecipe(string recipeName, string recipeId, RecipeIngredient[] ingredients,
        RecipeIngredient output, MachineVisualDefinition[] machineVisuals)
    {
        var recipeType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Recipes.Recipe", false))
            .FirstOrDefault(type => type != null);
        if (recipeType == null || !typeof(ScriptableObject).IsAssignableFrom(recipeType))
            return null;

        var recipeAsset = ScriptableObject.CreateInstance(recipeType);
        recipeAsset.name = recipeName;

        SetFieldValue(recipeType, recipeAsset, "id", recipeId);

        var recipeItemType = recipeType.Assembly.GetType("BigAmbitions.Factories.Recipes.RecipeItem");
        if (recipeItemType == null)
            return recipeAsset;

        SetCollectionField(
            recipeType,
            recipeAsset,
            "ingredients",
            recipeItemType,
            ingredients.Select(ingredient => CreateRecipeItem(recipeItemType, ingredient.ItemName, ingredient.Amount))
                .ToArray());

        SetFieldValue(
            recipeType,
            recipeAsset,
            "output",
            CreateRecipeItem(recipeItemType, output.ItemName, output.Amount));

        var machineVisualsField = recipeType.GetField("machineVisuals",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var machineVisualType = GetElementType(machineVisualsField?.FieldType);
        if (machineVisualsField != null && machineVisualType != null)
        {
            SetCollectionField(
                recipeType,
                recipeAsset,
                "machineVisuals",
                machineVisualType,
                machineVisuals.Select(machineVisual => CreateMachineVisual(
                    machineVisualType,
                    machineVisual.MachineName,
                    machineVisual.InputItemName,
                    machineVisual.OutputItemName)).ToArray());
        }

        return recipeAsset;
    }

    private static MachineVisualDefinition[] CreateGunRecipeMachineVisuals(string outputItemName)
    {
        return new[]
        {
            new MachineVisualDefinition(
                "ba:itemname_lasercuttingmachine",
                "gunstore-businesstype:itemname_gunpartscheap",
                "gunstore-businesstype:itemname_gunpartscheap"),
            new MachineVisualDefinition(
                "ba:itemname_consumergoodsassemblymachine",
                string.Empty,
                outputItemName)
        };
    }

    private static object CreateRecipeItem(Type recipeItemType, string itemName, int amount)
    {
        var recipeItem = Activator.CreateInstance(recipeItemType);
        if (recipeItem == null)
            throw new InvalidOperationException($"Could not create {recipeItemType.FullName}.");

        SetFieldValue(recipeItemType, recipeItem, "item", itemName);
        SetFieldValue(recipeItemType, recipeItem, "amount", amount);

        return recipeItem;
    }

    private static object CreateMachineVisual(Type machineVisualType, string machineName, string inputItemName,
        string outputItemName)
    {
        var machineVisual = Activator.CreateInstance(machineVisualType);
        if (machineVisual == null)
            throw new InvalidOperationException($"Could not create {machineVisualType.FullName}.");

        SetFieldValue(machineVisualType, machineVisual, "machineName", machineName);
        SetFieldValue(machineVisualType, machineVisual, "inputItemName", inputItemName);
        SetFieldValue(machineVisualType, machineVisual, "outputItemName", outputItemName);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorA", Color.clear);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorB", Color.clear);

        return machineVisual;
    }

    private static void SetCollectionField(Type ownerType, object owner, string fieldName, Type elementType,
        object[] values)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return;

        if (field.FieldType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var i = 0; i < values.Length; i++)
                array.SetValue(values[i], i);

            field.SetValue(owner, array);
            return;
        }

        var list = Activator.CreateInstance(field.FieldType) as IList;
        if (list == null)
            return;

        foreach (var value in values)
            list.Add(value);

        field.SetValue(owner, list);
    }

    private static Type? GetElementType(Type? collectionType)
    {
        if (collectionType == null)
            return null;

        if (collectionType.IsArray)
            return collectionType.GetElementType();

        return collectionType.IsGenericType ? collectionType.GetGenericArguments().FirstOrDefault() : null;
    }

    private static void SetFieldValue(Type ownerType, object owner, string fieldName, object? value)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            field.SetValue(owner, value);
    }

    private struct RecipeIngredient
    {
        public RecipeIngredient(string itemName, int amount)
        {
            ItemName = itemName;
            Amount = amount;
        }

        public string ItemName { get; }
        public int Amount { get; }
    }

    private struct MachineVisualDefinition
    {
        public MachineVisualDefinition(string machineName, string inputItemName, string outputItemName)
        {
            MachineName = machineName;
            InputItemName = inputItemName;
            OutputItemName = outputItemName;
        }

        public string MachineName { get; }
        public string InputItemName { get; }
        public string OutputItemName { get; }
    }
}
