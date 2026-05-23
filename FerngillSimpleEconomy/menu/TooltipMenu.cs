using System;
using System.Linq;
using System.Text;
using fse.core.helpers;
using fse.core.models;
using fse.core.services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace fse.core.menu;

public interface ITooltipMenu
{
	void PostRenderHud(RenderedHudEventArgs e);
	void PostRenderGui(RenderedActiveMenuEventArgs e);
}

public class TooltipMenu(
	IEconomyService econService,
	IBetterGameMenuService betterGameMenuService
	) : ITooltipMenu
{
	public void PostRenderHud(RenderedHudEventArgs e)
	{
		if (!ConfigModel.Instance.EnableTooltip || Game1.activeClickableMenu != null)
		{
			return;
		}

		var toolbarItem = Game1.onScreenMenus.OfType<Toolbar>().FirstOrDefault()?.hoverItem;
		
		PopulateHoverTextBoxAndDraw(toolbarItem);
	}

	public void PostRenderGui(RenderedActiveMenuEventArgs e)
	{
		if (!ConfigModel.Instance.EnableTooltip)
		{
			return;
		}
		if (Game1.activeClickableMenu == null)
		{
			return;
		}

		var item = GetHoveredItemFromMenu(Game1.activeClickableMenu);
		if (item != null)
		{
			PopulateHoverTextBoxAndDraw(item);
		}
	}

	private Item? GetHoveredItemFromMenu(IClickableMenu menu)
	{
		var page = betterGameMenuService.GetCurrentPage(menu);
		if (page is InventoryPage inventoryPage)
		{
			return inventoryPage.hoveredItem;
		}

		if (menu is MenuWithInventory inventoryMenu)
		{
			return inventoryMenu.hoveredItem;
		}

		return null;
	}

	private void PopulateHoverTextBoxAndDraw(Item? item)
	{
		if (item is not StardewValley.Object obj)
		{
			return;
		}
		var model = econService.GetItemModelFromObject(obj);

		if (model == null)
		{
			return;
		}

		DrawHoverTextBox(obj, model);
	}

	private void DrawHoverTextBox(Item item, ItemModel model)
	{
		var consolidated = econService.GetConsolidatedItem(model);
		const int height = 58;
		var vanillaTooltip = EstimateVanillaTooltipBounds(item);
		var safeArea = Utility.getSafeArea();
		var width = Math.Max(vanillaTooltip.Width, 178);
		var x = vanillaTooltip.X;
		var y = vanillaTooltip.Bottom + 10;

		if (x + width > safeArea.Right)
		{
			x = safeArea.Right - width;
		}

		if (x < safeArea.Left)
		{
			x = safeArea.Left;
		}

		if (y + height > safeArea.Bottom)
		{
			y = vanillaTooltip.Y - height - 10;
		}

		if (y < safeArea.Top)
		{
			y = safeArea.Top;
		}

		IClickableMenu.drawTextureBox(Game1.spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White);
		DrawSupplySummary(Game1.spriteBatch, consolidated, x + 22, y + 15);
	}

	private static void DrawSupplySummary(SpriteBatch batch, ItemModel model, int x, int y)
	{
		if (Game1.smallFont == null)
		{
			return;
		}

		var pricePercent = GetPriceMultiplierPercent(model.Supply);
		var percentText = $"{pricePercent}%";

		DrawCoinIcon(batch, new Rectangle(x, y + 3, 24, 24));
		batch.DrawString(Game1.smallFont, percentText, new Vector2(x + 30, y), Game1.textColor, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
		var textWidth = (int)Game1.smallFont.MeasureString(percentText).X;
		DrawTrendGlyphs(batch, new Rectangle(x + 42 + textWidth, y + 1, GetTrendGlyphCount(model.DailyDelta) * 22 + 4, 26), model.DailyDelta);
	}

	private static int GetPriceMultiplierPercent(int supply)
	{
		var maxSupply = Math.Max(ConfigModel.Instance.MaxCalculatedSupply, 1);
		var cappedSupply = Math.Min(Math.Max(supply, ConfigModel.MinSupply), maxSupply);
		var ratio = 1m - cappedSupply / (decimal)maxSupply;
		var multiplier = ratio * (ConfigModel.Instance.MaxPercentage - ConfigModel.Instance.MinPercentage) + ConfigModel.Instance.MinPercentage;
		return (int)Math.Round(multiplier * 100m, MidpointRounding.AwayFromZero);
	}

	private static void DrawTrendGlyphs(SpriteBatch batch, Rectangle bounds, int dailyDelta)
	{
		if (dailyDelta == 0 || Game1.mouseCursors == null)
		{
			DrawFilledRect(batch, new Rectangle(bounds.X + 6, bounds.Center.Y - 2, bounds.Width - 12, 4), Color.Black);
			return;
		}

		var source = dailyDelta > 0 ? new Rectangle(365, 495, 12, 11) : new Rectangle(352, 495, 12, 11);
		var color = dailyDelta > 0 ? Color.ForestGreen : Color.IndianRed;
		var count = GetTrendGlyphCount(dailyDelta);
		var arrowWidth = Math.Max(bounds.Width / count, 20);
		for (var i = 0; i < count; i++)
		{
			batch.Draw(Game1.mouseCursors, new Rectangle(bounds.X + i * (arrowWidth - 2), bounds.Y, arrowWidth, bounds.Height), source, color);
		}
	}

	private static int GetTrendGlyphCount(int dailyDelta)
	{
		var threshold = Math.Max(ConfigModel.Instance.DeltaArrow, 1);
		var magnitude = Math.Abs(dailyDelta);
		if (magnitude > threshold * 2)
		{
			return 3;
		}

		if (magnitude > threshold)
		{
			return 2;
		}

		return 1;
	}

	private static void DrawFilledRect(SpriteBatch batch, Rectangle bounds, Color color)
	{
		var texture = Game1.staminaRect ?? Game1.mouseCursors;
		if (texture == null)
		{
			return;
		}

		batch.Draw(texture, bounds, color);
	}

	private static void DrawCoinIcon(SpriteBatch batch, Rectangle bounds)
	{
		if (Game1.mouseCursors == null)
		{
			return;
		}

		batch.Draw(Game1.mouseCursors, bounds, new Rectangle(193, 373, 9, 10), Color.White);
	}

	private static Rectangle EstimateVanillaTooltipBounds(Item item)
	{
		var descFont = Game1.smallFont;
		var titleFont = Game1.dialogueFont;
		var description = item.getDescription();
		var title = item.DisplayName;
		var category = item is StardewValley.Object obj ? obj.getCategoryName() : "";

		var width = Math.Max((int)descFont.MeasureString(description).X, (int)titleFont.MeasureString(title).X) + 36;
		var height = (int)descFont.MeasureString(description).Y + 32 + (int)titleFont.MeasureString(title).Y + 16;

		if (item is FishingRod)
		{
			var slots = item.attachmentSlots();
			if (slots == 1)
			{
				height += 68;
			}
			else if (slots > 1)
			{
				height += 144;
			}
		}
		else
		{
			height += 68 * item.attachmentSlots();
		}

		if (!string.IsNullOrEmpty(category))
		{
			width = Math.Max(width, (int)descFont.MeasureString(category).X + 32);
			height += (int)descFont.MeasureString("T").Y;
		}

		try
		{
			var descBuilder = new StringBuilder(description);
			var extra = item.getExtraSpaceNeededForTooltipSpecialIcons(descFont, width, 92, height, descBuilder, title, -1);
			if (extra.X != 0)
			{
				width = extra.X;
			}

			if (extra.Y != 0)
			{
				height = extra.Y;
			}
		}
		catch
		{
			// Some modded items throw while estimating extra tooltip rows; the base size is still usable.
		}

		if (item is StardewValley.Object { Edibility: not (-300 or 0) } edible && item is not MeleeWeapon)
		{
			var staminaRecovery = edible.staminaRecoveredOnConsumption();
			var healthRecovery = edible.healthRecoveredOnConsumption();
			height += 40 * (staminaRecovery > 0 && healthRecovery > 0 ? 2 : 1);
		}

		if (item is StardewValley.Object sellable && (sellable.canBeShipped() || Utility.getSellToStorePriceOfItem(sellable, false) >= 0))
		{
			height += (int)Math.Max(descFont.MeasureString("0").Y + 4f, 44f);
		}

		height = Math.Max(height, 60);
		var x = Game1.getOldMouseX() + 32;
		var y = Game1.getOldMouseY() + 32;

		if (IsHoldingItemOnCursor())
		{
			x += 40;
			y += 40;
		}

		var safeArea = Utility.getSafeArea();
		if (x + width > safeArea.Right)
		{
			x = safeArea.Right - width;
			y += 16;
		}

		if (y + height > safeArea.Bottom)
		{
			x += 16;
			if (x + width > safeArea.Right)
			{
				x = safeArea.Right - width;
			}

			y = safeArea.Bottom - height;
		}

		return new Rectangle(x, y, width, height);
	}

	private static bool IsHoldingItemOnCursor()
	{
		if (Game1.player.CursorSlotItem != null)
		{
			return true;
		}

		return Game1.activeClickableMenu is MenuWithInventory { heldItem: not null };
	}
}
