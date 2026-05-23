using System;
using fse.core.models;
using fse.core.services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace fse.core.helpers;

public interface IDrawSupplyBarHelper
{
	public void DrawSupplyBar(SpriteBatch batch, int startingX, int startingY, int endingX, int barHeight, ItemModel model);
	public void DrawShopSupplyPreview(SpriteBatch batch, int startingX, int startingY, ItemModel model);
}

public class DrawSupplyBarHelper(IEconomyService economyService) : IDrawSupplyBarHelper
{ 
	private decimal? _breakEvenSupply;
	
	public void DrawSupplyBar(SpriteBatch batch, int startingX, int startingY, int endingX, int barHeight, ItemModel originalModel)
	{
		var model = economyService.GetConsolidatedItem(originalModel);
		const int maxBarHeight = 16;
		if (barHeight > maxBarHeight)
		{
			startingY += (barHeight - maxBarHeight) / 2;
			barHeight = maxBarHeight;
		}
		var scale = Math.Max(barHeight / 18f, 0.65f);
		var scaleInt = new Func<int, int>(value => (int)Math.Round(value * scale));
		var barWidth = Math.Max(((endingX - startingX) / 10) * 10, 20);
		var percentage = Math.Min(model.Supply / (float)ConfigModel.Instance.MaxCalculatedSupply, 1);
		var innerHeight = Math.Max(barHeight - scaleInt(6), scaleInt(8));
		var innerY = startingY + scaleInt(3);
		var innerX = startingX + scaleInt(4);
		var innerWidth = Math.Max(barWidth - scaleInt(8), scaleInt(10));
		var percentageWidth = (int)(innerWidth * percentage);

		var percentageRect = new Rectangle(innerX, innerY, percentageWidth, innerHeight);
			
		var color1 = new Color((int)(72 + percentage * 92), (int)(174 - percentage * 95), (int)(80 - percentage * 58));
		var color2 = new Color((int)(36 + percentage * 80), (int)(126 - percentage * 82), (int)(54 - percentage * 36));
		var color3 = new Color((int)(27 + percentage * 54), (int)(80 - percentage * 54), (int)(39 - percentage * 24));
		
		// bar background
		batch.Draw(Game1.staminaRect, new Rectangle(startingX, startingY, barWidth, barHeight), new Color(92, 64, 30));
		batch.Draw(Game1.staminaRect, new Rectangle(startingX + scaleInt(2), startingY + scaleInt(2), barWidth - scaleInt(4), barHeight - scaleInt(4)), new Color(167, 122, 70));
		batch.Draw(Game1.staminaRect, new Rectangle(innerX, innerY, innerWidth, innerHeight), new Color(106, 86, 48));
		
		// bar foreground
		if (percentageWidth > scaleInt(2))
		{
			batch.Draw(Game1.staminaRect, percentageRect, color2);
			batch.Draw(Game1.staminaRect, new Rectangle(innerX, innerY, percentageWidth, Math.Max(scaleInt(3), 1)), color1);
			batch.Draw(Game1.staminaRect, new Rectangle(innerX, innerY + innerHeight - Math.Max(scaleInt(3), 1), percentageWidth, Math.Max(scaleInt(3), 1)), color3);
		}

		// ticks 
		for (var i = 1; i < 10; i++)
		{
			var tickX = innerX + ((innerWidth / 10) * i);
			batch.Draw(Game1.staminaRect, new Rectangle(tickX, innerY, Math.Max(scaleInt(1), 1), innerHeight), Color.Black * 0.35f);
		}

		_breakEvenSupply ??= economyService.GetBreakEvenSupply();
		
		if (_breakEvenSupply.Value > 0)
		{
			var evenX = (int) Math.Floor((innerX + innerWidth * _breakEvenSupply.Value / ConfigModel.Instance.MaxCalculatedSupply));
			
			batch.Draw(Game1.mouseCursors, new Rectangle(evenX - scaleInt(5), startingY - scaleInt(9), scaleInt(18), scaleInt(16)), new Rectangle(232, 347, 9, 8), Color.White);
		}
		DrawDeltaArrows(batch, model, percentageRect, scale);
	}

	public void DrawShopSupplyPreview(SpriteBatch batch, int startingX, int startingY, ItemModel originalModel)
	{
		var model = economyService.GetConsolidatedItem(originalModel);
		if (Game1.dialogueFont == null)
		{
			return;
		}

		const float scale = 1f;
		var priceText = $"{GetPriceMultiplierPercent(model.Supply)}%";

		DrawCoinIcon(batch, new Rectangle(startingX, startingY + 8, 28, 28));
		batch.DrawString(Game1.dialogueFont, priceText, new Vector2(startingX + 36, startingY - 1), Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);

		var trendX = startingX + 146;
		DrawTrendGlyphs(batch, new Rectangle(trendX, startingY + 1, GetTrendGlyphCount(model.DailyDelta) * 36 + 8, 42), model.DailyDelta);
	}

	private static void DrawDeltaArrows(SpriteBatch batch, ItemModel model, Rectangle percentageRect, float scale)
	{
		var scaleInt = new Func<int, int>(value => (int)Math.Round(value * scale));
		var location = new Rectangle(percentageRect.X + percentageRect.Width - scaleInt(6),
			percentageRect.Y - scaleInt(17), scaleInt(5 * Game1.pixelZoom), scaleInt(5 * Game1.pixelZoom));
		
		if (model.DailyDelta < 0)
		{
			var leftArrow = new ClickableTextureComponent("left-arrow", location, "", "", Game1.mouseCursors,
				new Rectangle(352, 495, 12, 11), Game1.pixelZoom * .75f * scale);
			leftArrow.bounds.X -= scaleInt(18);
			if (model.DailyDelta < -2 * ConfigModel.Instance.DeltaArrow)
			{
				leftArrow.bounds.X += scaleInt(8);
				leftArrow.draw(batch);
			}

			if (model.DailyDelta < -1 * ConfigModel.Instance.DeltaArrow)
			{
				leftArrow.bounds.X += scaleInt(8);
				leftArrow.draw(batch);
			}

			leftArrow.bounds.X += scaleInt(8);
			leftArrow.draw(batch);
		}
		else
		{
			var rightArrow = new ClickableTextureComponent("right-arrow", location, "", "", Game1.mouseCursors,
				new Rectangle(365, 495, 12, 11), Game1.pixelZoom * .75f * scale);
			if (model.DailyDelta > 2 * ConfigModel.Instance.DeltaArrow)
			{
				rightArrow.bounds.X -= scaleInt(8);
				rightArrow.draw(batch);
			}

			if (model.DailyDelta > ConfigModel.Instance.DeltaArrow)
			{
				rightArrow.bounds.X -= scaleInt(8);
				rightArrow.draw(batch);
			}

			rightArrow.bounds.X -= scaleInt(8);
			rightArrow.draw(batch);
		}
	}

	private static int GetPriceMultiplierPercent(int supply)
	{
		var maxSupply = Math.Max(ConfigModel.Instance.MaxCalculatedSupply, 1);
		var cappedSupply = Math.Min(Math.Max(supply, ConfigModel.MinSupply), maxSupply);
		var ratio = 1m - cappedSupply / (decimal)maxSupply;
		var multiplier = ratio * (ConfigModel.Instance.MaxPercentage - ConfigModel.Instance.MinPercentage) + ConfigModel.Instance.MinPercentage;
		return (int)Math.Round(multiplier * 100m, MidpointRounding.AwayFromZero);
	}

	private static int GetSupplyPercent(int supply)
	{
		var maxSupply = Math.Max(ConfigModel.Instance.MaxCalculatedSupply, 1);
		var cappedSupply = Math.Min(Math.Max(supply, ConfigModel.MinSupply), maxSupply);
		return (int)Math.Round(cappedSupply / (decimal)maxSupply * 100m, MidpointRounding.AwayFromZero);
	}

	private static void DrawCoinIcon(SpriteBatch batch, Rectangle bounds)
	{
		if (Game1.mouseCursors == null)
		{
			return;
		}

		batch.Draw(Game1.mouseCursors, bounds, new Rectangle(193, 373, 9, 10), Color.White);
	}

	private static void DrawStockIcon(SpriteBatch batch, Rectangle bounds)
	{
		if (Game1.bigCraftableSpriteSheet != null)
		{
			var source = Game1.getSourceRectForStandardTileSheet(Game1.bigCraftableSpriteSheet, 130, 16, 32);
			var chestBounds = new Rectangle(bounds.X, bounds.Y - bounds.Height / 2, bounds.Width, bounds.Height * 2);
			batch.Draw(Game1.bigCraftableSpriteSheet, chestBounds, source, Color.White);
		}
	}

	private static void DrawTrendGlyphs(SpriteBatch batch, Rectangle bounds, int dailyDelta)
	{
		if (dailyDelta == 0 || Game1.mouseCursors == null)
		{
			DrawFilledRect(batch, new Rectangle(bounds.X + 6, bounds.Center.Y - 2, bounds.Width - 12, 4), new Color(132, 94, 48));
			return;
		}

		var source = dailyDelta > 0 ? new Rectangle(365, 495, 12, 11) : new Rectangle(352, 495, 12, 11);
		var color = dailyDelta > 0 ? Color.ForestGreen : Color.IndianRed;
		var count = GetTrendGlyphCount(dailyDelta);
		var arrowWidth = Math.Max(bounds.Width / count, 18);
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
}
