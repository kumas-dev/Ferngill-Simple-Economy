using fse.core.helpers;
using fse.core.menu;
using fse.core.models;
using fse.core.services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using StardewModdingAPI;
using StardewValley;
using Tests.HarmonyMocks;
using Tests.Mocks;

namespace Tests.menu;

public class ForecastMenuTests : HarmonyTestBase
{
	private SpriteBatch _batch;
	private Mock<IDrawTextHelper> _drawTextHelperMock;
	private Mock<IEconomyService> _economyServiceMock;
	private Mock<IModHelper> _helperMock;
	private Mock<IDrawSupplyBarHelper> _drawSupplyBarHelperMock;
	private ForecastMenu _menu;

	[SetUp]
	public override void Setup()
	{
		base.Setup();

		_helperMock = new Mock<IModHelper>();
		_economyServiceMock = new Mock<IEconomyService>();
		_drawTextHelperMock = new Mock<IDrawTextHelper>();
		_drawSupplyBarHelperMock = new Mock<IDrawSupplyBarHelper>();
		typeof(ForecastMenu).GetField("_cachedChosenCategory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, null);
		typeof(ForecastMenu).GetField("_cachedChosenSort", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, null);
		typeof(ForecastMenu).GetField("_textFilter", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, "");

		_helperMock.Setup(m => m.Translation).Returns(new MockTranslationHelper());

		ConfigModel.Instance = new ConfigModel
		{
			MinDelta = 0,
			MaxDelta = 1000,
		};

		_economyServiceMock.Setup(x => x.Loaded).Returns(true);
		_economyServiceMock.Setup(x => x.GetCategories()).Returns(new Dictionary<int, string>
		{
			{ 1, "Category1" },
			{ 2, "Category2" },
			{ 3, "Category3" },
			{ 4, string.Empty },
		});
		_economyServiceMock.Setup(m => m.GetItemsForCategory(1)).Returns(
			[
				new ItemModel("1") { Supply = 100, DailyDelta = 100 },
				new ItemModel("2") { Supply = 200, DailyDelta = 200 },
			]
		);
		_economyServiceMock.Setup(m => m.GetItemsForCategory(2)).Returns(
			[
				new ItemModel("3") { Supply = 300, DailyDelta = 300 },
				new ItemModel("4") { Supply = 400, DailyDelta = 400 },
				new ItemModel("5") { Supply = 500, DailyDelta = 500 },
			]
		);
		_economyServiceMock.Setup(m => m.GetItemsForCategory(3)).Returns(
			[
				new ItemModel("6") { Supply = 600, DailyDelta = 600 },
			]
		);

		_economyServiceMock.Setup(m => m.GetConsolidatedItem(It.IsAny<ItemModel>())).Returns<ItemModel>(i => i);

		_economyServiceMock.Setup(m => m.ItemValidForSeason(It.IsAny<ItemModel>(), It.IsAny<Seasons>())).Returns(true);

		HarmonyGame.GetOptionsResult = new Options();
		Game1.graphics = new GraphicsDeviceManager(null);
		Game1.staminaRect = new Texture2D(null, 0, 0);
		Game1.mouseCursors = new Texture2D(null, 0, 0);

		_batch = new SpriteBatch(null, 0);
		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);
	}

	[TearDown]
	public void Teardown()
	{
		_batch.Dispose();
		//clears out static objects
		_menu.gameWindowSizeChanged(new Rectangle(0, 0, 0, 0), new Rectangle(0, 0, 0, 0));
	}

	private void ExitAction()
	{
		
	}

	private void SelectCategoryByName(string categoryName)
	{
		Game1.options.gamepadControls = false;
		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var categoryDropdown = _menu.allClickableComponents.First(c => c.myID == 300);
		_menu.receiveLeftClick(categoryDropdown.bounds.Center.X, categoryDropdown.bounds.Center.Y);
		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var categoryRow = _menu.allClickableComponents.First(c => c.label == categoryName);
		_menu.receiveLeftClick(categoryRow.bounds.Center.X, categoryRow.bounds.Center.Y);
	}

	[TestCase(1080, 620, 780, 436, 150, 92)]
	[TestCase(10800, 6200, 1560, 1680, 4620, 2260)]
	public void ShouldSetupPositionAndSizeAndDrawBackground
	(
		int screenWidth,
		int screenHeight,
		int expectedWidth,
		int expectedHeight,
		int expectedX,
		int expectedY
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		_menu.draw(_batch);

		var calls = HarmonyGame.DrawDialogueBoxCalls;

		Assert.Multiple(() =>
		{
			Assert.That(_menu.width, Is.GreaterThan(0), "Menu width should be positive");
			Assert.That(_menu.height, Is.GreaterThan(0), "Menu height should be positive");
			Assert.That(_menu.xPositionOnScreen, Is.GreaterThanOrEqualTo(0), "Menu x-coordinate should be on screen");
			Assert.That(_menu.yPositionOnScreen, Is.GreaterThanOrEqualTo(0), "Menu y-coordinate should be on screen");
			Assert.That(calls, Has.Count.EqualTo(1), "Dialogue box not drawn");
			Assert.That(calls[0].x, Is.EqualTo(_menu.xPositionOnScreen), "Dialogue box x-coordinate should match the menu");
			Assert.That(calls[0].y, Is.EqualTo(_menu.yPositionOnScreen), "Dialogue box y-coordinate should match the menu");
			Assert.That(calls[0].width, Is.EqualTo(_menu.width), "Dialogue box width should match the menu");
			Assert.That(calls[0].height, Is.EqualTo(_menu.height), "Dialogue box height should match the menu");
		});
	}

	[Test]
	public void ShouldKeepMenuSizeWhenNoItemsMatch()
	{
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 1200;
		Game1.options.gamepadControls = true;

		_menu.draw(_batch);
		var populatedBounds = new Rectangle(_menu.xPositionOnScreen, _menu.yPositionOnScreen, _menu.width, _menu.height);
		var populatedCategoryRows = _menu.allClickableComponents
			.Where(c => c.myID == 300)
			.Select(c => c.bounds)
			.ToArray();

		_economyServiceMock.Setup(m => m.GetItemsForCategory(It.IsAny<int>())).Returns([]);
		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);
		HarmonyGame.DrawDialogueBoxCalls.Clear();
		_drawTextHelperMock.Invocations.Clear();

		_menu.draw(_batch);
		var emptyBounds = new Rectangle(_menu.xPositionOnScreen, _menu.yPositionOnScreen, _menu.width, _menu.height);
		var emptyCategoryRows = _menu.allClickableComponents
			.Where(c => c.myID == 300)
			.Select(c => c.bounds)
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(emptyBounds, Is.EqualTo(populatedBounds), "Menu outer bounds should not shrink when no rows match");
			Assert.That(emptyCategoryRows.First().X, Is.EqualTo(populatedCategoryRows.First().X), "Category panel should keep its X position");
			Assert.That(emptyCategoryRows.First().Width, Is.EqualTo(populatedCategoryRows.First().Width), "Category panel should keep its row width");
		});

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			It.IsAny<int>(),
			It.IsAny<int>(),
			"translation-fse.forecast.menu.empty",
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			false
		), Times.Once);
	}

	[TestCase(1080, 620, 540, 136)]
	[TestCase(10800, 6200, 5400, 2304)]
	public void ShouldDrawTitleInCorrectPosition
	(
		int screenWidth,
		int screenHeight,
		int expectedX,
		int expectedY
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		_menu.draw(_batch);

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			expectedX,
			It.IsAny<int>(),
			"translation-fse.forecast.menu.header.title",
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			true
		));
	}

	[TestCase(1080, 620, 2, 0, 995, 129, 44, 48, 995, 506, 44, 48, 1007, 181, 24, 40, 1007, 181, 24, 318)]
	[TestCase(10800, 6200, 2, 0, 6375, 2179, 44, 48, 6375, 4036, 44, 48, 6387, 2231, 24, 40, 6387, 2231, 24, 1798)]
	[TestCase(1080, 620, 10, 0, 995, 129, 44, 48, 995, 506, 44, 48, 1007, 181, 24, 40, 1007, 181, 24, 318)]
	[TestCase(10800, 6200, 10, 0, 6375, 2179, 44, 48, 6375, 4036, 44, 48, 6387, 2231, 24, 40, 6387, 2231, 24, 1798)]
	[TestCase(1080, 620, 10, 3, 995, 129, 44, 48, 995, 506, 44, 48, 1007, 274, 24, 40, 1007, 181, 24, 318)]
	[TestCase(10800, 6200, 10, 3, 6375, 2179, 44, 48, 6375, 4036, 44, 48, 6387, 3989, 24, 40, 6387, 2231, 24, 1798)]
	public void ShouldDrawScrollBarElementsInCorrectPosition
	(
		int screenWidth,
		int screenHeight,
		int numItems,
		int itemIndex,
		int upArrowExpectedX,
		int upArrowExpectedY,
		int upArrowExpectedWidth,
		int upArrowExpectedHeight,
		int downArrowExpectedX,
		int downArrowExpectedY,
		int downArrowExpectedWidth,
		int downArrowExpectedHeight,
		int barExpectedX,
		int barExpectedY,
		int barExpectedWidth,
		int barExpectedHeight,
		int runnerExpectedX,
		int runnerExpectedY,
		int runnerExpectedWidth,
		int runnerExpectedHeight
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		var models = new List<ItemModel>();
		for (var i = 0; i < numItems; i++)
		{
			models.Add(new ItemModel(i.ToString()) { DailyDelta = i * 100, Supply = i * 100 });
		}

		_economyServiceMock.Setup(m => m.GetItemsForCategory(1)).Returns(models.ToArray);
		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);

		_menu.draw(_batch);

		for (var i = 0; i < itemIndex; i++)
		{
			_menu.receiveScrollWheelAction(-1);
		}

		_menu.draw(_batch);

		var upArrow = HarmonyClickableTextureComponent.DrawCalls.First(p => p.Key.name == "up-arrow");
		var downArrow = HarmonyClickableTextureComponent.DrawCalls.First(p => p.Key.name == "down-arrow");
		var scrollbar = HarmonyClickableTextureComponent.DrawCalls.First(p => p.Key.name == "scrollbar");
		var runner = HarmonyIClickableMenu.DrawTextureBoxCalls.First().Value.First();

		Assert.Multiple(() =>
		{
			Assert.That(upArrow.Value, Is.EqualTo(2));
			Assert.That(downArrow.Value, Is.EqualTo(2));
			Assert.That(scrollbar.Value, Is.EqualTo(2));

			Assert.That(upArrow.Key.bounds.X, Is.GreaterThan(_menu.xPositionOnScreen), "Up Arrow X should be inside the menu");
			Assert.That(upArrow.Key.bounds.Y, Is.GreaterThan(_menu.yPositionOnScreen), "Up Arrow Y should be inside the menu");
			Assert.That(upArrow.Key.bounds.Width, Is.EqualTo(upArrowExpectedWidth), "Up Arrow width does not match expectation");
			Assert.That(upArrow.Key.bounds.Height, Is.EqualTo(upArrowExpectedHeight),
				"Up Arrow height does not match expectation");

			Assert.That(downArrow.Key.bounds.X, Is.EqualTo(upArrow.Key.bounds.X),
				"Down Arrow X should align with the up arrow");
			Assert.That(downArrow.Key.bounds.Y, Is.GreaterThan(upArrow.Key.bounds.Y),
				"Down Arrow should be below the up arrow");
			Assert.That(downArrow.Key.bounds.Width, Is.EqualTo(downArrowExpectedWidth),
				"Down Arrow width does not match expectation");
			Assert.That(downArrow.Key.bounds.Height, Is.EqualTo(downArrowExpectedHeight),
				"Down Arrow height does not match expectation");

			Assert.That(scrollbar.Key.bounds.X, Is.GreaterThan(upArrow.Key.bounds.X), "Scrollbar should sit inside the scroll gutter");
			Assert.That(scrollbar.Key.bounds.Y, Is.GreaterThanOrEqualTo(upArrow.Key.bounds.Y + upArrow.Key.bounds.Height), "Scrollbar should start below the up arrow");
			Assert.That(scrollbar.Key.bounds.Width, Is.EqualTo(barExpectedWidth), "Scrollbar width does not match expectation");
			Assert.That(scrollbar.Key.bounds.Height, Is.EqualTo(barExpectedHeight),
				"Scrollbar height does not match expectation");

			Assert.That(runner.x, Is.EqualTo(scrollbar.Key.bounds.X), "Runner X should align with the scrollbar");
			Assert.That(runner.y, Is.GreaterThanOrEqualTo(upArrow.Key.bounds.Y + upArrow.Key.bounds.Height), "Runner should start below the up arrow");
			Assert.That(runner.width, Is.EqualTo(runnerExpectedWidth), "Runner width does not match expectation");
			Assert.That(runner.height, Is.GreaterThanOrEqualTo(scrollbar.Key.bounds.Height), "Runner should be tall enough for the scrollbar");
		});
	}

	[TestCase(1080, 620, 208, 262, 516, 636, 694, 436, 92)]
	[TestCase(10800, 6200, 2376, 2430, 5066, 5396, 5573, 1680, 2260)]
	public void ShouldDrawStaticPartitions
	(
		int screenWidth,
		int screenHeight,
		int expectedFirstHorizontalY,
		int expectedSecondHorizontalY,
		int expectedFirstVerticalX,
		int expectedSecondVerticalX,
		int expectedThirdVerticalX,
		int expectedModifiedHeight,
		int expectedModifiedY
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		_menu.draw(_batch);

		var lineCalls = HarmonySpriteBatch.DrawCalls[_batch]
			.Where(call => call.texture == Game1.staminaRect)
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(lineCalls.Length, Is.GreaterThanOrEqualTo(5), "Table chrome should draw horizontal and vertical guide lines");
			Assert.That(lineCalls.Any(call => call.destinationRectangle is { } rect && rect.Width > rect.Height), Is.True, "Table should draw horizontal lines");
			Assert.That(lineCalls.Any(call => call.destinationRectangle is { } rect && rect.Height > rect.Width), Is.True, "Table should draw vertical lines");
		});
	}

	[TestCase(1080, 620, 234, 617, 697, 753, 811)]
	[TestCase(10800, 6200, 2402, 5272, 5516, 5688, 5931)]
	public void ShouldDrawHeader
	(
		int screenWidth,
		int screenHeight,
		int expectedY,
		int expectedHeaderX,
		int expectedSellPriceX,
		int expectedPerDayX,
		int expectedSupplyX
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		_menu.draw(_batch);

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			It.IsAny<int>(),
			It.IsAny<int>(),
			"translation-fse.forecast.menu.header.item",
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			false
		));

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			It.IsAny<int>(),
			It.IsAny<int>(),
			"translation-fse.forecast.menu.header.sell",
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			false
		));

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			It.IsAny<int>(),
			It.IsAny<int>(),
			"translation-fse.forecast.menu.header.supplyShort",
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			false
		));
	}

	[TestCase(1080, 620, 182, 230, 182, 198)]
	[TestCase(10800, 6200, 4652, 2398, 4652, 2366)]
	public void ShouldDrawCategoryDropdown
	(
		int screenWidth,
		int screenHeight,
		int expectedX,
		int expectedY,
		int expectedLabelX,
		int expectedLabelY
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;
		Game1.options.gamepadControls = true;

		_menu.draw(_batch);

		var categoryDropdown = _menu.allClickableComponents.Single(c => c.myID == 300);

		Assert.Multiple(() =>
		{
			Assert.That(HarmonyOptionsDropDown.DrawCalls, Is.Empty, "Category should no longer use vanilla dropdown");
			Assert.That(categoryDropdown.bounds.X, Is.GreaterThan(_menu.xPositionOnScreen));
			Assert.That(categoryDropdown.bounds.Y, Is.LessThan(_menu.yPositionOnScreen), "CJB-style toolbar controls should sit above the menu frame");
			Assert.That(categoryDropdown.bounds.Right, Is.LessThan(_menu.xPositionOnScreen + _menu.width));
			Assert.That(categoryDropdown.bounds.Height, Is.EqualTo(56));
			Assert.That(categoryDropdown.label, Is.EqualTo("translation-fse.forecast.menu.allCategory"));
			Assert.That(HarmonySpriteBatch.DrawCalls[_batch].Count(call => call.sourceRectangle == new Rectangle(16, 368, 16, 16)), Is.EqualTo(0), "Forecast filters should not use game menu tab sprites");
			Assert.That(HarmonyIClickableMenu.DrawTextureBoxCalls[_batch].Any(call => call.sourceRect == new Rectangle(0, 256, 60, 60)), Is.True);
		});
	}

	[TestCase(1080, 620, 182, 536, 182, 504)]
	[TestCase(10800, 6200, 4652, 2704, 4652, 2672)]
	public void ShouldDrawSortingButton
	(
		int screenWidth,
		int screenHeight,
		int expectedX,
		int expectedY,
		int expectedLabelX,
		int expectedLabelY
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;
		Game1.options.gamepadControls = true;

		_menu.draw(_batch);

		var sortButton = _menu.allClickableComponents.Single(c => c.myID == 200);
		var categoryDropdown = _menu.allClickableComponents.Single(c => c.myID == 300);

		Assert.Multiple(() =>
		{
			Assert.That(HarmonyOptionsDropDown.DrawCalls, Is.Empty, "Sort should no longer use vanilla dropdown");
			Assert.That(sortButton.name, Is.EqualTo("Supply"));
			Assert.That(sortButton.label, Is.EqualTo("translation-fse.forecast.menu.sort.supply"));
			Assert.That(sortButton.bounds.X, Is.GreaterThan(_menu.xPositionOnScreen));
			Assert.That(sortButton.bounds.Right, Is.LessThan(categoryDropdown.bounds.X));
			Assert.That(sortButton.bounds.Height, Is.EqualTo(56));
			Assert.That(HarmonyIClickableMenu.DrawTextureBoxCalls[_batch].Count(call => call.sourceRect == new Rectangle(0, 256, 60, 60)), Is.GreaterThanOrEqualTo(3), "Toolbar controls should render as Stardew-style tab boxes");
		});

		_drawTextHelperMock.Verify(m => m.DrawAlignedText(
			_batch,
			It.IsAny<int>(),
			It.IsAny<int>(),
			"translation-fse.forecast.menu.sort.marketPricePerDay",
			It.IsAny<DrawTextHelper.DrawTextAlignment>(),
			It.IsAny<DrawTextHelper.DrawTextAlignment>(),
			It.IsAny<bool>()
		), Times.Never);
	}

	[TestCase(Season.Spring, 1080, 620, 316, 324, 328, 334, 336, true, false, false, false)]
	[TestCase(Season.Spring, 10800, 6200, 4826, 4834, 4838, 4844, 2504, true, false, false, false)]
	[TestCase(Season.Summer, 1080, 620, 316, 324, 328, 334, 336, false, true, false, false)]
	[TestCase(Season.Summer, 10800, 6200, 4826, 4834, 4838, 4844, 2504, false, true, false, false)]
	[TestCase(Season.Fall, 1080, 620, 316, 324, 328, 334, 336, false, false, true, false)]
	[TestCase(Season.Fall, 10800, 6200, 4826, 4834, 4838, 4844, 2504, false, false, true, false)]
	[TestCase(Season.Winter, 1080, 620, 316, 324, 328, 334, 336, false, false, false, true)]
	[TestCase(Season.Winter, 10800, 6200, 4826, 4834, 4838, 4844, 2504, false, false, false, true)]
	public void ShouldDrawSeasonTabs
	(
		Season season,
		int screenWidth,
		int screenHeight,
		int checkbox1X,
		int checkbox2X,
		int checkbox3X,
		int checkbox4X,
		int checkboxY,
		bool checkbox1Checked,
		bool checkbox2Checked,
		bool checkbox3Checked,
		bool checkbox4Checked
	)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;
		Game1.season = season;
		Game1.options.gamepadControls = true;

		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);

		_menu.draw(_batch);

		var seasonTabs = _menu.allClickableComponents
			.Where(c => c.myID is >= 102 and <= 105)
			.OrderBy(c => c.myID)
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(HarmonyOptionsCheckbox.DrawCalls, Is.Empty, "Seasons should no longer use vanilla checkboxes");
			Assert.That(seasonTabs, Has.Length.EqualTo(4));
			Assert.That(seasonTabs.All(c => c.bounds.X >= _menu.xPositionOnScreen), Is.True, "Season toggles should stay in the top toolbar");
			Assert.That(seasonTabs.All(c => c.bounds.Y < _menu.yPositionOnScreen), Is.True, "Season toggles should sit above the menu frame");
			Assert.That(seasonTabs[0].bounds.X, Is.LessThan(seasonTabs[1].bounds.X));
			Assert.That(seasonTabs[1].bounds.X, Is.LessThan(seasonTabs[2].bounds.X));
			Assert.That(seasonTabs[2].bounds.X, Is.LessThan(seasonTabs[3].bounds.X));
			Assert.That(seasonTabs.All(c => c.bounds.Y == seasonTabs[0].bounds.Y), Is.True);
			Assert.That(seasonTabs.All(c => c.bounds is { Width: 48, Height: 48 }), Is.True);
			Assert.That(HarmonySpriteBatch.DrawCalls[_batch].Count(call => call.sourceRectangle == new Rectangle(16, 368, 16, 16)), Is.EqualTo(0), "Forecast filters should not use game menu tab sprites");
		});
	}

	[TestCase(1080, 620, 894, 84)]
	[TestCase(10800, 6200, 6144, 2252)]
	public void ShouldDrawExitButton(int screenWidth, int screenHeight, int x, int y)
	{
		Game1.uiViewport.Width = screenWidth;
		Game1.uiViewport.Height = screenHeight;

		_menu.draw(_batch);

		var exitButton = HarmonyClickableTextureComponent.DrawCalls.FirstOrDefault(c => c.Key.name == "exit-button").Key;
		Assert.Multiple(() =>
		{
			Assert.That(exitButton.bounds.X, Is.GreaterThan(_menu.xPositionOnScreen));
			Assert.That(exitButton.bounds.Y, Is.LessThanOrEqualTo(_menu.yPositionOnScreen + _menu.height));
		});
	}

	[TestCase(1,  1000, 10,  209, 825, 1077)]
	[TestCase(1,  100, -1,  209, 825, 1077)]
	[TestCase(2,  1000, 10,  209, 825, 1077)]
	public void ShouldDrawRow
	(
		int expectedRows,
		int sellPrice,
		int sellPricePerDay,
		int expectedNameLocation,
		int expectedPriceLocation,
		int expectedPerDayLocation)
	{
		ConfigModel.Instance.MinDelta = -1000;
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 100 + 355 + 120 * expectedRows;

		var models = new List<ItemModel>
		{
			new(sellPrice.ToString()) { Supply = 321, DailyDelta = 12 },
			new((sellPrice + 1).ToString()) { Supply = 654, DailyDelta = -7 },
		};

		_economyServiceMock.Setup(m => m.GetPricePerDay(models[0])).Returns(sellPricePerDay);
		_economyServiceMock.Setup(m => m.GetPricePerDay(models[1])).Returns(sellPricePerDay);

		_economyServiceMock.Setup(m => m.GetItemsForCategory(1)).Returns(models.ToArray);
		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);
		Game1.staminaRect = new Texture2D(null, 0, 0);

		SelectCategoryByName("Category1");

		HarmonyObject.DrawInMenuCalls.Clear();
		HarmonyIClickableMenu.DrawHoriztonalPartitionCalls.Clear();
		HarmonySpriteBatch.DrawCalls.Clear();
		HarmonyClickableTextureComponent.DrawCalls.Clear();
		_drawTextHelperMock.Invocations.Clear();
		_drawSupplyBarHelperMock.Invocations.Clear();

		_menu.draw(_batch);

		var drawIconLocation = HarmonyObject.DrawInMenuCalls[models[0].GetObjectInstance(1)].Last();
		Assert.Multiple(() =>
		{
			Assert.That(drawIconLocation.X, Is.GreaterThan(_menu.xPositionOnScreen));
			Assert.That(drawIconLocation.Y, Is.GreaterThan(_menu.yPositionOnScreen));
		});

		Assert.That(HarmonySpriteBatch.DrawCalls[_batch].Count(call => call.texture == Game1.staminaRect), Is.GreaterThanOrEqualTo(expectedRows), "Rows should draw separators without relying on exact chrome partition counts");
		_drawSupplyBarHelperMock.Verify(s => s.DrawSupplyBar
		(
			It.IsAny<SpriteBatch>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<ItemModel>()
		), Times.Never);
		

		_drawTextHelperMock.Verify(m => m.DrawAlignedText
			(
				_batch,
				It.IsAny<int>(),
				It.IsAny<int>(),
				$"display-{sellPrice}",
				DrawTextHelper.DrawTextAlignment.Start,
				DrawTextHelper.DrawTextAlignment.Middle,
				false
		), Times.AtLeastOnce
		);

		_drawTextHelperMock.Verify(m => m.DrawAlignedText
			(
				_batch,
				It.IsAny<int>(),
				It.IsAny<int>(),
				$"display-{sellPrice+1}",
				DrawTextHelper.DrawTextAlignment.Start,
				DrawTextHelper.DrawTextAlignment.Middle,
				false
			), Times.Once
		);

		Assert.That(HarmonyObject.DrawInMenuCalls.Count, Is.GreaterThanOrEqualTo(expectedRows), "Rows should still render item content with the compact supply summary");
	}

	[TestCase(0, 6)]
	[TestCase(1, 2)]
	[TestCase(2, 3)]
	[TestCase(3, 1)]
	public void ShouldDrawCorrectNumberOfRowsForCategory(int selectedOption, int expectedRows)
	{
		ConfigModel.Instance.MinDelta = -1000;
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 6200;

		_menu = new ForecastMenu(_helperMock.Object, _economyServiceMock.Object, _drawTextHelperMock.Object, _drawSupplyBarHelperMock.Object, ExitAction);

		if (selectedOption > 0)
		{
			SelectCategoryByName($"Category{selectedOption}");
		}

		HarmonySpriteBatch.DrawCalls.Clear();
		_drawSupplyBarHelperMock.Invocations.Clear();
		_drawTextHelperMock.Invocations.Clear();
		_menu.draw(_batch);

		_drawSupplyBarHelperMock.Verify(s => s.DrawSupplyBar(
			It.IsAny<SpriteBatch>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<ItemModel>()
		), Times.Never);
		Assert.That(CountDisplayedRows(), Is.EqualTo(expectedRows));
	}

	[Test]
	public void ShouldCycleSortWithSingleToolbarButton()
	{
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 1200;
		Game1.options.gamepadControls = false;

		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var sortButton = _menu.allClickableComponents.Single(c => c.myID == 200);

		_menu.receiveLeftClick(sortButton.bounds.Center.X, sortButton.bounds.Center.Y);
		_menu.draw(_batch);
		_menu.populateClickableComponentList();

		Assert.That(_menu.allClickableComponents.Single(c => c.myID == 200).name, Is.EqualTo("DailyChange"));
	}

	[Test]
	public void ShouldOpenCategoryDropdownAndSelectThirdEntry()
	{
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 1200;
		Game1.options.gamepadControls = false;

		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var category = _menu.allClickableComponents.Single(c => c.myID == 300);
		_menu.receiveLeftClick(category.bounds.Center.X, category.bounds.Center.Y);

		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var thirdCategory = _menu.allClickableComponents.Single(c => c.label == "Category2");
		_menu.receiveLeftClick(thirdCategory.bounds.Center.X, thirdCategory.bounds.Center.Y);

		HarmonySpriteBatch.DrawCalls.Clear();
		_drawSupplyBarHelperMock.Invocations.Clear();
		_drawTextHelperMock.Invocations.Clear();
		_menu.draw(_batch);

		_drawSupplyBarHelperMock.Verify(s => s.DrawSupplyBar(
			It.IsAny<SpriteBatch>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<int>(),
			It.IsAny<ItemModel>()
		), Times.Never);
		Assert.That(CountDisplayedRows(), Is.EqualTo(3));
	}

	private int CountDisplayedRows()
	{
		return _drawTextHelperMock.Invocations.Count(invocation =>
			invocation.Method.Name == nameof(IDrawTextHelper.DrawAlignedText)
			&& invocation.Arguments.Count > 3
			&& invocation.Arguments[3] is string text
			&& text.StartsWith("display-", StringComparison.Ordinal)
		);
	}

	[Test]
	public void ShouldNotOpenControllerKeyboardWhenSearchBoxIsClickedWithMouse()
	{
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 1200;
		Game1.options.gamepadControls = false;

		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		var search = _menu.allClickableComponents.Single(c => c.myID == 400);

		_menu.receiveLeftClick(search.bounds.Center.X, search.bounds.Center.Y);

		Assert.Multiple(() =>
		{
			Assert.That(HarmonyGame.ShowTextEntryCalls, Is.Empty);
		});
	}

	[Test]
	public void ShouldReopenTextEntryWhenSearchBoxIsSelectedAgainWithController()
	{
		Game1.uiViewport.Width = 2000;
		Game1.uiViewport.Height = 1200;
		Game1.options.gamepadControls = true;

		_menu.draw(_batch);
		_menu.populateClickableComponentList();
		_menu.setCurrentlySnappedComponentTo(400);

		_menu.receiveGamePadButton(Buttons.A);
		_menu.receiveGamePadButton(Buttons.B);
		_menu.receiveGamePadButton(Buttons.A);

		Assert.Multiple(() =>
		{
			Assert.That(HarmonyGame.ShowTextEntryCalls, Has.Count.EqualTo(2));
		});
	}
}
