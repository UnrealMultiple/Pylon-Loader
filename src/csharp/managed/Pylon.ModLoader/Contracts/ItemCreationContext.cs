using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>物品被创建时的上下文（TML 的 <c>ItemCreationContext</c>）：制作、掉落、商店等来源各自派生。</summary>
public abstract record class ItemCreationContext;

/// <summary>制作台做出物品。</summary>
public record class RecipeItemCreationContext(Recipe Recipe, List<Item> ConsumedItems, Item DestinationStack) : ItemCreationContext;
