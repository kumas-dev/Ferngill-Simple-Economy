using System;
using System.Collections.Generic;
using System.Linq;
using fse.core.helpers;
using fse.core.models;
using fse.core.services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace fse.core.menu;

public abstract class IForecastMenu : IClickableMenu;

public class ForecastMenu : IForecastMenu
{
	private readonly IModHelper _helper;
	private readonly IEconomyService _economyService;
	private readonly IDrawTextHelper _drawTextHelper;
	private readonly Action? _exitAction;
	private ItemModel[] _allItems;
	private readonly Dictionary<int, string> _categories;
	private readonly List<KeyValuePair<int, string>> _categoryOptions;
	private int _itemIndex;
	private int _maxNumberOfRows;
	private bool _isScrolling;
	private ClickableTextureComponent? _upArrow;
	private ClickableTextureComponent? _downArrow;
	private ClickableTextureComponent? _scrollbar;
	private Rectangle? _scrollbarRunner;
	private int _bottomIndex;
	private ClickableComponent[]? _seasonComponents;
	private ClickableComponent? _sortButton;
	private ClickableComponent? _searchBoxComponent;
	private ClickableTextureComponent? _exitButton;
	private ForecastDropdown<int>? _categoryDropdown;
	private TextBox? _searchBox;
	private bool _drawn;
	private int _lastWidth;
	private int _lastHeight;
	private string? _hoverText;
	private Texture2D? _stockMenuTexture;
	private ForecastMenuLayout _layout;

	private string Name => _helper.Translation.Get("fse.forecast.menu.sort.name");
	private string MarketPrice => _helper.Translation.Get("fse.forecast.menu.sort.marketPrice");
	private string Supply => _helper.Translation.Get("fse.forecast.menu.sort.supply");
	private string DailyChange => _helper.Translation.Get("fse.forecast.menu.sort.delta");
		
	private readonly List<string> _sortOptions = [nameof(Supply), nameof(DailyChange), nameof(MarketPrice), nameof(Name)];
	private readonly List<string> _sortDisplayOptions;
	private string _chosenSort;
	private int _chosenCategory;
	private Seasons _chosenSeasons;

	private static int? _cachedChosenCategory;
	private static string? _cachedChosenSort;
	private static string _textFilter = "";

	private const int SpringComponentId = 102;
	private const int SummerComponentId = 103;
	private const int FallComponentId = 104;
	private const int WinterComponentId = 105;
	private const int UpArrowComponentId = 107;
	private const int DownArrowComponentId = 108;
	private const int SortComponentId = 200;
	private const int CategoryComponentId = 300;
	private const int SearchComponentId = 400;
	private const int DropdownComponentBaseId = 500;
	private const int ToolbarButtonHeight = 56;
	private const int SeasonButtonSize = 48;

	public ForecastMenu(
		IModHelper helper,
		IEconomyService economyService,
		IDrawTextHelper drawTextHelper,
		IDrawSupplyBarHelper drawSupplyBarHelper,
		Action? exitAction
	)
	{
		_helper = helper;
		_economyService = economyService;
		_drawTextHelper = drawTextHelper;
		_exitAction = exitAction;

		_sortDisplayOptions = [Supply, DailyChange, MarketPrice, Name];

		_chosenSeasons = SeasonHelper.GetCurrentSeason();

		_chosenSort = NormalizeSort(_cachedChosenSort);
			
		_allItems = [];
		_categories = new Dictionary<int, string>
		{
			{ int.MinValue, helper.Translation.Get("fse.forecast.menu.allCategory") },
		};
		_categoryOptions = [];

		if (!economyService.Loaded)
		{
			return;
		}

		economyService.GetCategories();
		_categories = _categories
			.Concat(economyService.GetCategories().Select(pair => new KeyValuePair<int, string>(pair.Key, GetCategoryDisplayName(pair.Key, pair.Value))))
			.ToDictionary(g => g.Key, g => g.Value);
				
		_categories = _categories.GroupBy(pair => pair.Value).ToDictionary(pairs => pairs.First().Key, pairs => pairs.First().Value);
		_categoryOptions = _categories.ToList();
		_chosenCategory = _cachedChosenCategory ?? int.MinValue;
		SetupItemsWithSort();
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
	{
		base.gameWindowSizeChanged(oldBounds, newBounds);
		_upArrow = null;
		_downArrow = null;
		_scrollbar = null;
		_scrollbarRunner = null;
		_seasonComponents = null;
		_sortButton = null;
		_searchBoxComponent = null;
		_categoryDropdown = null;
		_searchBox = null;
		_exitButton = null;
		_drawn = false;
	}

	public override void receiveScrollWheelAction(int direction)
	{
		base.receiveScrollWheelAction(direction);
		if (_categoryDropdown?.IsExpanded == true)
		{
			return;
		}

		var startingIndex = _itemIndex;
		_itemIndex = BoundsHelper.EnsureBounds(_itemIndex + (direction < 0 ? 1 : -1), 0, _bottomIndex);
		if (startingIndex != _itemIndex)
		{
			Game1.playSound("shwip");
		}
	}

	public override bool readyToClose() => _searchBox?.Selected != true && _categoryDropdown?.IsExpanded != true;

	public override void update(GameTime time)
	{
		base.update(time);
		_searchBox?.Update();
		var currentText = _searchBox?.Text ?? _textFilter;
		if (currentText != _textFilter)
		{
			_textFilter = currentText;
			SetupItemsWithSort();
			_itemIndex = 0;
		}
	}

	public override void receiveGamePadButton(Buttons button)
	{
		// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
		switch (button)
		{
			case Buttons.B:
				if (_searchBox?.Selected == true)
				{
					DeselectSearchBox();
					return;
				}
				if (_categoryDropdown?.IsExpanded == true)
				{
					SetCategoryDropdown(false);
					return;
				}
				GoBackToPreviousMenu();
				return;
			case Buttons.LeftTrigger:
				GoBackToPreviousMenu();
				return;
			case Buttons.A:
				ActivateCurrentSnappedComponent();
				return;
			case Buttons.LeftShoulder:
				ScrollRows(-_maxNumberOfRows);
				return;
			case Buttons.RightShoulder:
				ScrollRows(_maxNumberOfRows);
				return;
			case Buttons.RightTrigger:
				ScrollRows(_maxNumberOfRows);
				return;
			case Buttons.LeftThumbstickRight:
			case Buttons.DPadRight:
				MoveSnappedComponent(Game1.right);
				return;
			case Buttons.LeftThumbstickLeft:
			case Buttons.DPadLeft:
				MoveSnappedComponent(Game1.left);
				return;
			case Buttons.LeftThumbstickDown:
			case Buttons.DPadDown:
				MoveSnappedComponent(Game1.down);
				return;
			case Buttons.LeftThumbstickUp:
			case Buttons.DPadUp:
				MoveSnappedComponent(Game1.up);
				return;
		}

		base.receiveGamePadButton(button);
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		base.receiveLeftClick(x, y, playSound);
		var startingIndex = _itemIndex;
		if (!_drawn)
		{
			return;
		}

		if (_categoryDropdown?.TryClick(x, y, out var categoryClicked, out var dropdownToggled) == true)
		{
			if (dropdownToggled)
			{
				SetCategoryDropdown(_categoryDropdown.IsExpanded);
			}
			if (categoryClicked)
			{
				_chosenCategory = _categoryDropdown.Selected;
				_cachedChosenCategory = _chosenCategory;
				SetupItemsWithSort();
				_itemIndex = 0;
			}
			return;
		}
			
		if (_sortButton?.bounds.Contains(x, y) == true)
		{
			SelectNextSort();
			return;
		}
		if (_searchBoxComponent?.bounds.Contains(x, y) == true)
		{
			SelectSearchBox(showControllerKeyboard: false);
			return;
		}
		else if (_upArrow?.containsPoint(x, y) ?? false)
		{
			_itemIndex = BoundsHelper.EnsureBounds(_itemIndex - 1, 0, _bottomIndex);
		}
		else if (_downArrow?.containsPoint(x, y) ?? false)
		{
			_itemIndex = BoundsHelper.EnsureBounds(_itemIndex + 1, 0, _bottomIndex);
		}
		else if (_scrollbar?.containsPoint(x, y) ?? false)
		{
			_isScrolling = true;
		}
		else if (_exitButton?.containsPoint(x, y) ?? false)
		{
			GoBackToPreviousMenu();
		}

		if (_searchBox?.Selected == true)
		{
			DeselectSearchBox();
		}

		for (var i = 0; i < _seasonComponents?.Length; i++)
		{
			if (!_seasonComponents[i].bounds.Contains(x, y))
			{
				continue;
			}

			ToggleSeason(i);
			return;
		}

		if (startingIndex != _itemIndex)
		{
			Game1.playSound("shwip");
		}
	}

	private void ActivateCurrentSnappedComponent()
	{
		if (!_drawn)
		{
			return;
		}

		switch (currentlySnappedComponent?.myID)
		{
			case SpringComponentId:
				ToggleSeason(0);
				return;
			case SummerComponentId:
				ToggleSeason(1);
				return;
			case FallComponentId:
				ToggleSeason(2);
				return;
			case WinterComponentId:
				ToggleSeason(3);
				return;
			case IClickableMenu.upperRightCloseButton_ID:
				GoBackToPreviousMenu();
				return;
			case UpArrowComponentId:
				ScrollRows(-1);
				return;
			case DownArrowComponentId:
				ScrollRows(1);
				return;
			case SortComponentId:
				SelectNextSort();
				return;
			case CategoryComponentId:
				SetCategoryDropdown(_categoryDropdown?.IsExpanded != true);
				return;
			case SearchComponentId:
				SelectSearchBox(showControllerKeyboard: true);
				return;
		}

		if (currentlySnappedComponent?.myID is >= DropdownComponentBaseId and < DropdownComponentBaseId + 100)
		{
			if (_categoryDropdown?.TrySelectByComponentId(currentlySnappedComponent.myID) == true)
			{
				_chosenCategory = _categoryDropdown.Selected;
				_cachedChosenCategory = _chosenCategory;
				SetCategoryDropdown(false);
				SetupItemsWithSort();
				_itemIndex = 0;
			}
		}
	}

	private void ToggleSeason(int seasonIndex)
	{
		var flag = seasonIndex switch
		{
			0 => Seasons.Spring,
			1 => Seasons.Summer,
			2 => Seasons.Fall,
			3 => Seasons.Winter,
			_ => Seasons.Spring | Seasons.Summer | Seasons.Fall | Seasons.Winter,
		};

		if (_chosenSeasons.HasFlag(flag))
		{
			_chosenSeasons -= flag;
		}
		else
		{
			_chosenSeasons |= flag;
		}

		SetupItemsWithSort();
		Game1.playSound("drumkit6");
	}

	private void SelectSort(int index)
	{
		if (index < 0 || index >= _sortOptions.Count)
		{
			return;
		}

		_chosenSort = _sortOptions[index];
		_cachedChosenSort = _chosenSort;
		SetupItemsWithSort();
		_itemIndex = 0;
		Game1.playSound("drumkit6");
	}

	private string NormalizeSort(string? sort)
	{
		return !string.IsNullOrWhiteSpace(sort) && _sortOptions.Contains(sort)
			? sort
			: nameof(Supply);
	}

	private void SelectNextSort()
	{
		var index = _sortOptions.IndexOf(_chosenSort);
		SelectSort((index + 1 + _sortOptions.Count) % _sortOptions.Count);
	}

	private void SelectSearchBox(bool showControllerKeyboard)
	{
		if (_searchBox == null)
		{
			return;
		}

		_searchBox.Selected = true;
		if (showControllerKeyboard)
		{
			Game1.showTextEntry(_searchBox);
		}
	}

	private void DeselectSearchBox()
	{
		if (_searchBox == null)
		{
			return;
		}

		_textFilter = _searchBox.Text ?? "";
		_searchBox.Selected = false;
		Game1.closeTextEntry();
		SetupItemsWithSort();
		_itemIndex = 0;
	}

	private void SetCategoryDropdown(bool expanded)
	{
		if (_categoryDropdown == null)
		{
			return;
		}

		_categoryDropdown.IsExpanded = expanded;
		if (!expanded && Game1.options.gamepadControls)
		{
			setCurrentlySnappedComponentTo(CategoryComponentId);
			snapCursorToCurrentSnappedComponent();
		}
	}

	private void ScrollRows(int amount)
	{
		var startingIndex = _itemIndex;

		_itemIndex = BoundsHelper.EnsureBounds(_itemIndex + amount, 0, _bottomIndex);

		if (startingIndex != _itemIndex)
		{
			Game1.playSound("shwip");
		}
	}

	private void GoBackToPreviousMenu()
	{
		if (_exitAction != null)
		{
			_exitAction();
			return;
		}
		
		exitThisMenu();
	}

	public override void releaseLeftClick(int x, int y)
	{
		base.releaseLeftClick(x, y);
		_isScrolling = false;
		if (!_drawn)
		{
			return;
		}
	}

	public override void leftClickHeld(int x, int y)
	{
		base.leftClickHeld(x, y);
		if (!_drawn)
		{
			return;
		}
		if (!_isScrolling)
		{
			return;
		}

		if (!_scrollbarRunner.HasValue)
		{
			return;
		}

		if (_scrollbar == null)
		{
			return;
		}

		var startingIndex = _itemIndex;

		if (y < _scrollbarRunner.Value.Y)
		{
			_itemIndex = 0;
		}

		if (y > _scrollbarRunner.Value.Y + _scrollbarRunner.Value.Height)
		{
			_itemIndex = _bottomIndex;
		}
			
		if (_bottomIndex == 0)
		{
			return;
		}

		var totalBarLength = _scrollbarRunner.Value.Height - _scrollbar.bounds.Height;
		var step = totalBarLength / (float)_bottomIndex;

		var relativeMousePos = y - _scrollbarRunner.Value.Y;


		if (step == 0)
		{
			// should not be possible but a user report has a stacktrace that says it is.

			return;
		}

		_itemIndex = BoundsHelper.EnsureBounds((int)Math.Round(relativeMousePos / step) , 0, _bottomIndex);

		if (_itemIndex != startingIndex)
		{
			Game1.playSound("shwip");
		}
	}

	public override void draw(SpriteBatch batch)
	{
		UpdateLayout();
		_hoverText = null;
		DrawBackground(batch);
		DrawTitle(batch);
		DrawScrollBar(batch);
		DrawFilterToolbar(batch);
		DrawPartitions(batch);

		DrawHeader(batch);

		for (var i = 0; i < _maxNumberOfRows; i++)
		{
			if (_itemIndex + i < _allItems.Length)
			{
				DrawRow(batch, _allItems[_itemIndex + i], i, xPositionOnScreen, yPositionOnScreen, width);
			}
		}
		DrawEmptyState(batch);
		DrawDropdownOverlay(batch);
		DrawExitButton(batch);
		DrawHoverText(batch);
		if (Game1.options.gamepadControls)
		{
			populateClickableComponentList();
			if (!_drawn || currentlySnappedComponent == null)
			{
				snapToDefaultClickableComponent();
			}
		}
		DrawMouse(batch);
		_drawn = true;
	}

	private void UpdateLayout()
	{
		_layout = ForecastMenuLayout.Create(new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), _allItems.Length);
		width = _layout.OuterBounds.Width;
		height = _layout.OuterBounds.Height;
		xPositionOnScreen = _layout.OuterBounds.X;
		yPositionOnScreen = _layout.OuterBounds.Y;

		if (width != _lastWidth || height != _lastHeight)
		{
			ResetLayoutComponents();
		}

		_lastWidth = width;
		_lastHeight = height;

		_maxNumberOfRows = Math.Max((_layout.ListBounds.Height + _layout.RowPadding) / _layout.RowPitch, 1);
		_bottomIndex = Math.Max(_allItems.Length - _maxNumberOfRows, 0);
		_itemIndex = BoundsHelper.EnsureBounds(_itemIndex, 0, _bottomIndex);
	}

	private void ResetLayoutComponents()
	{
		_upArrow = null;
		_downArrow = null;
		_scrollbar = null;
		_scrollbarRunner = null;
		_seasonComponents = null;
		_sortButton = null;
		_searchBoxComponent = null;
		_categoryDropdown = null;
		_searchBox = null;
		_exitButton = null;
		_drawn = false;
	}

	private void DrawBackground(SpriteBatch batch)
	{
		Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);
		DrawTablePanel(batch);
	}

	private void DrawTablePanel(SpriteBatch batch)
	{
		var texture = Game1.menuTexture;
		if (texture == null)
		{
			return;
		}

		const int horizontalPadding = 20;
		const int verticalPadding = 12;
		var bounds = new Rectangle(
			_layout.HeaderBounds.X - horizontalPadding,
			_layout.HeaderBounds.Y - verticalPadding,
			_layout.HeaderBounds.Width + horizontalPadding * 2,
			_layout.ListBounds.Bottom - _layout.HeaderBounds.Y + verticalPadding * 2
		);
		IClickableMenu.drawTextureBox(batch, texture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 1f, false);
	}

	private void DrawPartitions(SpriteBatch batch)
	{
		var top = _layout.HeaderBounds.Y;
		var bottom = _layout.ListBounds.Bottom;
		DrawTableLine(batch, _layout.HeaderBounds.X, top, _layout.HeaderBounds.Width, 3);
		DrawTableLine(batch, _layout.ListBounds.X, _layout.ListBounds.Y, _layout.ListBounds.Width, 3);
		DrawTableLine(batch, _layout.PriceColumn.X, top, 3, bottom - top);
		DrawTableLine(batch, _layout.StockColumn.X, top, 3, bottom - top);
		DrawTableLine(batch, _layout.TrendColumn.X, top, 3, bottom - top);
	}

	private static void DrawTableLine(SpriteBatch batch, int x, int y, int width, int height)
	{
		var texture = Game1.staminaRect ?? Game1.mouseCursors;
		if (texture == null)
		{
			return;
		}

		batch.Draw(texture, new Rectangle(x, y, width, height), new Color(132, 70, 24) * 0.9f);
	}
			
	private void DrawHeader(SpriteBatch batch)
	{
		var yLoc = _layout.HeaderBounds.Center.Y;
			
		_drawTextHelper.DrawAlignedText(batch, _layout.ItemColumn.Center.X, yLoc, _helper.Translation.Get("fse.forecast.menu.header.item"), DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
		_drawTextHelper.DrawAlignedText(batch, _layout.PriceColumn.Center.X, yLoc, _helper.Translation.Get("fse.forecast.menu.header.sell"), DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
		_drawTextHelper.DrawAlignedText(batch, _layout.StockColumn.Center.X, yLoc, _helper.Translation.Get("fse.forecast.menu.header.supplyShort"), DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
		_drawTextHelper.DrawAlignedText(batch, _layout.TrendColumn.Center.X, yLoc, _helper.Translation.Get("fse.forecast.menu.header.trend"), DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
	}

	private void DrawTitle(SpriteBatch batch)
	{
		_drawTextHelper.DrawAlignedText(batch, _layout.TitleBounds.Center.X, _layout.TitleBounds.Center.Y, _helper.Translation.Get("fse.forecast.menu.header.title"), DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, true);
	}

	private void DrawFilterToolbar(SpriteBatch batch)
	{
		DrawSeasonTabs(batch);
		DrawSortButton(batch);
		DrawCategoryDropdown(batch);
		DrawSearchBox(batch);
	}

	private void DrawSeasonTabs(SpriteBatch batch)
	{
		var seasons = new[]
		{
			(Season: Seasons.Spring, Id: SpringComponentId, Name: "spring", Source: new Rectangle(406, 441, 12, 8), Label: _helper.Translation.Get("fse.forecast.menu.season.spring")),
			(Season: Seasons.Summer, Id: SummerComponentId, Name: "summer", Source: new Rectangle(406, 449, 12, 8), Label: _helper.Translation.Get("fse.forecast.menu.season.summer")),
			(Season: Seasons.Fall, Id: FallComponentId, Name: "fall", Source: new Rectangle(406, 457, 12, 8), Label: _helper.Translation.Get("fse.forecast.menu.season.fall")),
			(Season: Seasons.Winter, Id: WinterComponentId, Name: "winter", Source: new Rectangle(406, 465, 12, 8), Label: _helper.Translation.Get("fse.forecast.menu.season.winter")),
		};
		_seasonComponents ??= new ClickableComponent[seasons.Length];

		const int gap = 8;
		for (var i = 0; i < seasons.Length; i++)
		{
			var bounds = new Rectangle(_layout.SeasonControlsBounds.X + i * (SeasonButtonSize + gap), _layout.SeasonControlsBounds.Y, SeasonButtonSize, SeasonButtonSize);
			var selected = _chosenSeasons.HasFlag(seasons[i].Season);
			DrawIconButton(batch, bounds, Game1.mouseCursors, seasons[i].Source, selected, 3f, seasons[i].Label);

			_seasonComponents[i] ??= new ClickableComponent(bounds, seasons[i].Name) { myID = seasons[i].Id };
			_seasonComponents[i].bounds = bounds;
			_seasonComponents[i].label = seasons[i].Label;
		}
	}

	private void DrawSortButton(SpriteBatch batch)
	{
		var sortIndex = Math.Max(_sortOptions.IndexOf(_chosenSort), 0);
		var label = _sortDisplayOptions[sortIndex];
		DrawForecastTabBox(batch, _layout.SortBounds, out var textPos);
		DrawSortIcon(batch, new Rectangle(_layout.SortBounds.X + 12, _layout.SortBounds.Y + 12, 32, 32), sortIndex);
		if (Game1.smallFont != null)
		{
			batch.DrawString(Game1.smallFont, label, new Vector2(textPos.X + 38, textPos.Y), Game1.textColor);
		}
		_sortButton ??= new ClickableComponent(_layout.SortBounds, _chosenSort) { myID = SortComponentId };
		_sortButton.bounds = _layout.SortBounds;
		_sortButton.name = _chosenSort;
		_sortButton.label = label;
	}

	private void DrawCategoryDropdown(SpriteBatch batch)
	{
		_categoryDropdown ??= new ForecastDropdown<int>(
			_layout.CategoryBounds,
			_chosenCategory,
			_categoryOptions.Select(pair => pair.Key).ToArray(),
			value => _categories.TryGetValue(value, out var label) ? label : _helper.Translation.Get("fse.config.other"),
			DropdownComponentBaseId
		);
		_categoryDropdown.Bounds = _layout.CategoryBounds;
		_categoryDropdown.Selected = _chosenCategory;
		_categoryDropdown.Draw(batch, DrawForecastTabBox, false);
	}

	private void DrawDropdownOverlay(SpriteBatch batch)
	{
		if (_categoryDropdown?.IsExpanded != true)
		{
			return;
		}

		_categoryDropdown.DrawItems(batch, DrawForecastTabBox);
	}

	private void DrawSearchBox(SpriteBatch batch)
	{
		_searchBox ??= new TextBox(TryLoadTextBoxTexture(), null, Game1.smallFont, Game1.textColor)
		{
			Text = _textFilter,
		};
		_searchBox.X = _layout.SearchBounds.X + 12;
		_searchBox.Y = _layout.SearchBounds.Y + 10;
		_searchBox.Width = _layout.SearchBounds.Width - 48;
		_searchBox.Height = _layout.SearchBounds.Height - 16;
		DrawForecastTabBox(batch, _layout.SearchBounds, out _);
		if (Game1.smallFont != null)
		{
			_searchBox.Draw(batch);
		}

		if (Game1.mouseCursors != null)
		{
			batch.Draw(Game1.mouseCursors, new Vector2(_layout.SearchBounds.Right - 36, _layout.SearchBounds.Y + 12), new Rectangle(80, 0, 13, 13), Color.White, 0f, Vector2.Zero, 2.5f, SpriteEffects.None, 0.9f);
		}

		_searchBoxComponent ??= new ClickableComponent(_layout.SearchBounds, "search") { myID = SearchComponentId };
		_searchBoxComponent.bounds = _layout.SearchBounds;
		_searchBoxComponent.label = _helper.Translation.Get("fse.forecast.menu.filter.search");
	}

	private static Texture2D? TryLoadTextBoxTexture()
	{
		try
		{
			return Game1.content?.Load<Texture2D>("LooseSprites\\textBox");
		}
		catch
		{
			return null;
		}
	}
		
	private void DrawExitButton(SpriteBatch batch)
	{
		_exitButton ??= new ClickableTextureComponent(
			"exit-button",
			new Rectangle(
				_layout.CloseButtonBounds.X,
				_layout.CloseButtonBounds.Y,
				_layout.CloseButtonBounds.Width,
				_layout.CloseButtonBounds.Height
			),
			"",
			"",
			Game1.mouseCursors,
			new Rectangle(337, 494, 12, 12), 
			4f
		);
		_exitButton.myID = IClickableMenu.upperRightCloseButton_ID;
		

		_exitButton.draw(batch);
	}
		
	private void DrawSortIcon(SpriteBatch batch, Rectangle bounds, int sortIndex)
	{
		var stockTexture = GetStockMenuTexture();
		var icon = sortIndex switch
		{
			0 => (Texture: stockTexture, Source: new Rectangle(0, 0, 16, 16), Scale: 2f),
			1 => (Texture: Game1.mouseCursors, Source: new Rectangle(421, 459, 11, 12), Scale: 2.4f),
			2 => (Texture: Game1.mouseCursors, Source: new Rectangle(193, 373, 9, 10), Scale: 2.5f),
			3 => (Texture: Game1.mouseCursors, Source: new Rectangle(128, 400, 16, 16), Scale: 2f),
			_ => (Texture: Game1.mouseCursors, Source: new Rectangle(421, 472, 11, 12), Scale: 2.4f),
		};

		if (icon.Texture == null)
		{
			return;
		}

		batch.Draw(icon.Texture, new Vector2(bounds.Center.X - icon.Source.Width * icon.Scale / 2f, bounds.Center.Y - icon.Source.Height * icon.Scale / 2f), icon.Source, Color.White, 0f, Vector2.Zero, icon.Scale, SpriteEffects.None, 0.9f);
	}

	private void DrawIconButton(SpriteBatch batch, Rectangle bounds, Texture2D? iconTexture, Rectangle iconSource, bool selected, float iconScale, string hoverText)
	{
		DrawPanelBox(batch, bounds, selected ? Color.White : Color.White * 0.72f);

		if (iconTexture != null)
		{
			var iconWidth = iconSource.Width * iconScale;
			var iconHeight = iconSource.Height * iconScale;
			var iconPosition = new Vector2(bounds.Center.X - iconWidth / 2f, bounds.Center.Y - iconHeight / 2f);
			batch.Draw(iconTexture, iconPosition, iconSource, selected ? Color.White : Color.White * 0.62f, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0.9f);
		}

		if (IsHoveringTab(bounds))
		{
			_hoverText = hoverText;
		}
	}

	private Texture2D? GetStockMenuTexture()
	{
		if (_stockMenuTexture != null)
		{
			return _stockMenuTexture;
		}

		try
		{
			_stockMenuTexture = _helper.ModContent.Load<Texture2D>("assets/stock-menu.png");
		}
		catch
		{
			_stockMenuTexture = Game1.mouseCursors;
		}

		return _stockMenuTexture;
	}

	private bool IsHoveringTab(Rectangle bounds)
	{
		return bounds.Contains(Game1.getOldMouseX(), Game1.getOldMouseY())
			|| currentlySnappedComponent?.bounds == bounds;
	}

	private void DrawHoverText(SpriteBatch batch)
	{
		if (string.IsNullOrWhiteSpace(_hoverText) || Game1.smallFont == null)
		{
			return;
		}

		IClickableMenu.drawHoverText(batch, _hoverText, Game1.smallFont);
	}

	private void DrawPanelBox(SpriteBatch batch, Rectangle bounds, Color color)
	{
		var texture = Game1.menuTexture ?? Game1.mouseCursors ?? Game1.staminaRect;
		if (texture == null)
		{
			return;
		}

		IClickableMenu.drawTextureBox(batch, texture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, color, 1f, false);
	}

	private static void DrawForecastTabBox(SpriteBatch batch, Rectangle bounds, out Vector2 innerDrawPosition)
	{
		var texture = Game1.menuTexture ?? Game1.mouseCursors ?? Game1.staminaRect;
		innerDrawPosition = new Vector2(bounds.X + 16, bounds.Y + 14);
		if (texture == null)
		{
			return;
		}

		IClickableMenu.drawTextureBox(batch, texture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 1f, false);
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

	private string GetCategoryDisplayName(int categoryId, string categoryName)
	{
		if (!string.IsNullOrWhiteSpace(categoryName))
		{
			return categoryName.Trim();
		}

		return _helper.Translation.Get("fse.config.other");
	}

	private void DrawEmptyState(SpriteBatch batch)
	{
		if (_allItems.Length != 0)
		{
			return;
		}

		_drawTextHelper.DrawAlignedText(
			batch,
			_layout.ListBounds.Center.X,
			_layout.ListBounds.Center.Y,
			_helper.Translation.Get("fse.forecast.menu.empty"),
			DrawTextHelper.DrawTextAlignment.Middle,
			DrawTextHelper.DrawTextAlignment.Middle,
			false
		);
	}

	private void DrawRow(SpriteBatch batch, ItemModel model, int rowNumber, int startingX, int startingY, int rowWidth)
	{
		var obj = model.GetObjectInstance(1);

		var rowBounds = new Rectangle(_layout.ListBounds.X, _layout.ListBounds.Y + _layout.RowPitch * rowNumber, _layout.ListBounds.Width, _layout.RowHeight);
		var x = rowBounds.X + _layout.RowPadding;
		var iconY = rowBounds.Y + Math.Max((rowBounds.Height - 54) / 2, 0);

		var textCenterLine = rowBounds.Center.Y;

		obj.drawInMenu(batch, new Vector2(x, iconY), 0.85f);

		if (rowNumber != _maxNumberOfRows - 1)
		{
			DrawTableLine(batch, rowBounds.X, rowBounds.Bottom, rowBounds.Width, 3);
		}

		DrawPriceValues(batch, model, textCenterLine);
		DrawStockValues(batch, model, textCenterLine);
		DrawTrendValues(batch, model, textCenterLine);
		var textX = x + Game1.tileSize + 8;
		DrawSmallText(batch, obj.DisplayName, new Vector2(textX, textCenterLine - 15), DrawTextHelper.DrawTextAlignment.Start);
	}

	private void DrawPriceValues(SpriteBatch batch, ItemModel originalModel, int textCenterLine)
	{
		var model = _economyService.GetConsolidatedItem(originalModel);
		var pricePercent = GetPriceMultiplierPercent(model.Supply);

		if (Game1.smallFont == null)
		{
			_drawTextHelper.DrawAlignedText(batch, _layout.PriceColumn.Center.X, textCenterLine, $"{pricePercent}%", DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
			return;
		}

		const float textScale = 0.95f;
		var x = _layout.PriceColumn.X + 18;
		var y = textCenterLine - 15;
		DrawCoinIcon(batch, new Rectangle(x, textCenterLine - 12, 22, 24));
		batch.DrawString(Game1.smallFont, $"{pricePercent}%", new Vector2(x + 30, y), Game1.textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
	}

	private void DrawStockValues(SpriteBatch batch, ItemModel originalModel, int textCenterLine)
	{
		var model = _economyService.GetConsolidatedItem(originalModel);
		var supplyPercent = GetSupplyPercent(model.Supply);

		if (Game1.smallFont == null)
		{
			_drawTextHelper.DrawAlignedText(batch, _layout.StockColumn.Center.X, textCenterLine, $"{supplyPercent}%", DrawTextHelper.DrawTextAlignment.Middle, DrawTextHelper.DrawTextAlignment.Middle, false);
			return;
		}

		const float textScale = 0.95f;
		var y = textCenterLine - 15;
		var supplyX = _layout.StockColumn.X + 16;
		DrawStockIcon(batch, new Rectangle(supplyX, textCenterLine - 13, 26, 26));
		batch.DrawString(Game1.smallFont, $"{supplyPercent}%", new Vector2(supplyX + 34, y), Game1.textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
	}

	private void DrawTrendValues(SpriteBatch batch, ItemModel originalModel, int textCenterLine)
	{
		var model = _economyService.GetConsolidatedItem(originalModel);
		var trendGlyphWidth = GetTrendGlyphCount(model.DailyDelta) * 24 + 6;
		var trendX = _layout.TrendColumn.Center.X - trendGlyphWidth / 2;
		DrawTrendGlyphs(batch, new Rectangle(trendX, textCenterLine - 15, trendGlyphWidth, 30), model.DailyDelta);
	}

	private void DrawSmallText(SpriteBatch batch, string text, Vector2 position, DrawTextHelper.DrawTextAlignment horizontalAlignment)
	{
		if (Game1.smallFont == null)
		{
			_drawTextHelper.DrawAlignedText(batch, (int)position.X, (int)position.Y, text, horizontalAlignment, DrawTextHelper.DrawTextAlignment.Middle, false);
			return;
		}

		const float scale = 0.95f;
		var adjustedPosition = horizontalAlignment switch
		{
			DrawTextHelper.DrawTextAlignment.Middle => new Vector2(position.X - Game1.smallFont.MeasureString(text).X * scale / 2f, position.Y),
			DrawTextHelper.DrawTextAlignment.End => new Vector2(position.X - Game1.smallFont.MeasureString(text).X * scale, position.Y),
			_ => position,
		};

		batch.DrawString(Game1.smallFont, text, adjustedPosition, Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
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

	private void DrawStockIcon(SpriteBatch batch, Rectangle bounds)
	{
		if (Game1.bigCraftableSpriteSheet != null)
		{
			var source = Game1.getSourceRectForStandardTileSheet(Game1.bigCraftableSpriteSheet, 130, 16, 32);
			var chestBounds = new Rectangle(bounds.X, bounds.Y - bounds.Height / 2, bounds.Width, bounds.Height * 2);
			batch.Draw(Game1.bigCraftableSpriteSheet, chestBounds, source, Color.White);
			return;
		}

		DrawFilledRect(batch, bounds, Color.ForestGreen * 0.85f);
	}

	private static void DrawTrendIcon(SpriteBatch batch, Rectangle bounds, int dailyDelta)
	{
		if (Game1.mouseCursors == null || dailyDelta == 0)
		{
			if (dailyDelta == 0)
			{
				DrawFilledRect(batch, new Rectangle(bounds.X + 3, bounds.Center.Y - 2, bounds.Width - 6, 4), new Color(132, 94, 48));
			}
			return;
		}

		var source = dailyDelta > 0 ? new Rectangle(365, 495, 12, 11) : new Rectangle(352, 495, 12, 11);
		var color = dailyDelta > 0 ? Color.ForestGreen : Color.IndianRed;
		batch.Draw(Game1.mouseCursors, bounds, source, color);
	}

	private static void DrawTrendGlyphs(SpriteBatch batch, Rectangle bounds, int dailyDelta)
	{
		if (dailyDelta == 0)
		{
			DrawFilledRect(batch, new Rectangle(bounds.X + 6, bounds.Center.Y - 2, bounds.Width - 12, 4), new Color(132, 94, 48));
			return;
		}

		var count = GetTrendGlyphCount(dailyDelta);
		var arrowWidth = Math.Max(bounds.Width / count, 18);
		for (var i = 0; i < count; i++)
		{
			DrawTrendIcon(batch, new Rectangle(bounds.X + i * (arrowWidth - 2), bounds.Y, arrowWidth, bounds.Height), dailyDelta);
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

		return dailyDelta == 0 ? 1 : 1;
	}


	private void DrawScrollBar(SpriteBatch batch)
	{
		if (_upArrow == null || _downArrow == null || _scrollbar == null || _scrollbarRunner == null)
		{
			_upArrow = new ClickableTextureComponent("up-arrow", _layout.UpArrowBounds, "", "", Game1.mouseCursors, new Rectangle(421, 459, 11, 12), Game1.pixelZoom)
			{
				myID = UpArrowComponentId,
				upNeighborID = SortComponentId,
				downNeighborID = DownArrowComponentId,
			};
			_downArrow = new ClickableTextureComponent("down-arrow", _layout.DownArrowBounds, "", "", Game1.mouseCursors, new Rectangle(421, 472, 11, 12), Game1.pixelZoom)
			{
				myID = DownArrowComponentId,
				upNeighborID = UpArrowComponentId,
				downNeighborID = IClickableMenu.upperRightCloseButton_ID,
			};
			_scrollbar = new ClickableTextureComponent("scrollbar", _layout.ScrollbarBounds, "", "", Game1.mouseCursors, new Rectangle(435, 463, 6, 10), Game1.pixelZoom);
			_scrollbarRunner = _layout.ScrollRunnerBounds;
		}
			
		var totalBarLength = _scrollbarRunner.Value.Height - _scrollbar.bounds.Height;
		var step = _bottomIndex == 0 ? 0 : totalBarLength / (float)_bottomIndex;

		if (_itemIndex == _bottomIndex || _bottomIndex == 0)
		{
			_scrollbar.bounds.Y = _scrollbarRunner.Value.Y + _scrollbarRunner.Value.Height - _scrollbar.bounds.Height;
		}
		else
		{
			_scrollbar.bounds.Y = _scrollbarRunner.Value.Y + (int)Math.Round(step * _itemIndex);
		}
			
		drawTextureBox(
			batch, 
			Game1.mouseCursors, 
			new Rectangle(403, 383, 6, 6), 
			_scrollbarRunner.Value.X, 
			_scrollbarRunner.Value.Y, 
			_scrollbarRunner.Value.Width, 
			_scrollbarRunner.Value.Height, 
			Color.White, 
			Game1.pixelZoom,
			false
		);
		_upArrow.draw(batch);
		_downArrow.draw(batch);
		_scrollbar.draw(batch);
	}

	public override void populateClickableComponentList()
	{
		allClickableComponents = [];

		if (_seasonComponents != null)
		{
			allClickableComponents.AddRange(_seasonComponents);
		}

		if (_sortButton != null)
		{
			allClickableComponents.Add(_sortButton);
		}

		if (_categoryDropdown != null)
		{
			allClickableComponents.Add(_categoryDropdown);
			if (_categoryDropdown.IsExpanded)
			{
				allClickableComponents.AddRange(_categoryDropdown.GetChildComponents());
			}
		}

		if (_searchBoxComponent != null)
		{
			allClickableComponents.Add(_searchBoxComponent);
		}

		if (_upArrow != null)
		{
			allClickableComponents.Add(_upArrow);
		}

		if (_downArrow != null)
		{
			allClickableComponents.Add(_downArrow);
		}

		if (_exitButton != null)
		{
			allClickableComponents.Add(_exitButton);
		}

	}

	public override void snapToDefaultClickableComponent()
	{
		setCurrentlySnappedComponentTo(SpringComponentId);
		base.snapToDefaultClickableComponent();
	}

	private void MoveSnappedComponent(int direction)
	{
		if (currentlySnappedComponent == null)
		{
			snapToDefaultClickableComponent();
		}

		var oldId = currentlySnappedComponent?.myID ?? SpringComponentId;
		customSnapBehavior(direction, 0, oldId);
	}

	protected override void customSnapBehavior(int direction, int oldRegion, int oldId)
	{
		if (oldId >= DropdownComponentBaseId && oldId < DropdownComponentBaseId + 100)
		{
			var nextId = oldId + (direction == Game1.down ? 1 : direction == Game1.up ? -1 : 0);
			setCurrentlySnappedComponentTo(direction switch
			{
				Game1.up or Game1.down when _categoryDropdown?.ContainsComponentId(nextId) == true => nextId,
				Game1.left => SortComponentId,
				Game1.right => SearchComponentId,
				_ => oldId,
			});
			snapCursorToCurrentSnappedComponent();
			return;
		}

		switch (oldId)
		{
			case SpringComponentId:
			case SummerComponentId:
			case FallComponentId:
			case WinterComponentId:
				var nextSeasonId = oldId + (direction == Game1.right ? 1 : direction == Game1.left ? -1 : 0);
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.left or Game1.right when nextSeasonId is >= SpringComponentId and <= WinterComponentId => nextSeasonId,
					Game1.right => SortComponentId,
					Game1.down => UpArrowComponentId,
					Game1.up => IClickableMenu.upperRightCloseButton_ID,
					_ => oldId,
				});
				break;
			case UpArrowComponentId:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.up => SortComponentId,
					Game1.left => SearchComponentId,
					Game1.down => DownArrowComponentId,
					_ => oldId,
				});
				break;
			case DownArrowComponentId:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.up => UpArrowComponentId,
					Game1.down => IClickableMenu.upperRightCloseButton_ID,
					_ => oldId,
				});
				break;
			case IClickableMenu.upperRightCloseButton_ID:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.down => SpringComponentId,
					Game1.up => DownArrowComponentId,
					_ => oldId,
				});
				break;
			case SortComponentId:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.left => WinterComponentId,
					Game1.right => CategoryComponentId,
					Game1.down => UpArrowComponentId,
					Game1.up => IClickableMenu.upperRightCloseButton_ID,
					_ => oldId,
				});
				break;
			case CategoryComponentId:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.left => SortComponentId,
					Game1.right => SearchComponentId,
					Game1.down when _categoryDropdown?.IsExpanded == true => DropdownComponentBaseId,
					Game1.down => UpArrowComponentId,
					Game1.up => IClickableMenu.upperRightCloseButton_ID,
					_ => oldId,
				});
				break;
			case SearchComponentId:
				setCurrentlySnappedComponentTo(direction switch
				{
					Game1.left => CategoryComponentId,
					Game1.right => UpArrowComponentId,
					Game1.down => UpArrowComponentId,
					Game1.up => IClickableMenu.upperRightCloseButton_ID,
					_ => oldId,
				});
				break;
			default:
				base.customSnapBehavior(direction, oldRegion, oldId);
				break;
		}

		snapCursorToCurrentSnappedComponent();
	}

	private void DrawMouse(SpriteBatch batch)
	{
		if (!Game1.options.hardwareCursor)
		{
			batch.Draw(
				Game1.mouseCursors, 
				new Vector2(Game1.getOldMouseX(), Game1.getOldMouseY()), 
				Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, Game1.options.gamepadControls ? 44 : 0, 16, 16), 
				Color.White, 
				0f, 
				Vector2.Zero, 
				Game1.pixelZoom + Game1.dialogueButtonScale / 150f, 
				SpriteEffects.None, 
				1f
			);
		}
	}

	private readonly record struct ForecastMenuLayout(
		Rectangle OuterBounds,
		Rectangle TitleBounds,
		Rectangle CloseButtonBounds,
		Rectangle CategoryBounds,
		Rectangle SortBounds,
		Rectangle SearchBounds,
		Rectangle SeasonControlsBounds,
		Rectangle HeaderBounds,
		Rectangle ListBounds,
		Rectangle ItemColumn,
		Rectangle PriceColumn,
		Rectangle StockColumn,
		Rectangle TrendColumn,
		Rectangle UpArrowBounds,
		Rectangle DownArrowBounds,
		Rectangle ScrollbarBounds,
		Rectangle ScrollRunnerBounds,
		int CategoryLabelY,
		int SortLabelY,
		int RowHeight,
		int RowPadding
	)
	{
		public int RowPitch => RowHeight + RowPadding;

		public static ForecastMenuLayout Create(Rectangle viewport, int itemCount)
		{
			const int minMarginX = 64;
			const int minMarginY = 130;
			const int maxWidth = 1260;
			const int maxHeight = 1040;
			const int contentInset = 24;
			const int titleHeight = 52;
			const int headerHeight = 50;
			const int rowHeight = 64;
			const int rowPadding = 3;

			var viewportWidth = Math.Max(viewport.Width, 640);
			var viewportHeight = Math.Max(viewport.Height, 480);
			var availableWidth = Math.Max(viewportWidth - minMarginX * 2, 480);
			var availableHeight = Math.Max(viewportHeight - minMarginY * 2, 360);
			var width = Math.Min(availableWidth, maxWidth);
			var maxMenuHeight = Math.Min(availableHeight, maxHeight);
			const int contentTopInset = 8;
			const int contentBottomInset = 24;
			var fixedHeight = contentTopInset + contentBottomInset + headerHeight;
			var visibleRows = Math.Max((maxMenuHeight - fixedHeight + rowPadding) / (rowHeight + rowPadding), 1);
			var listHeight = visibleRows * (rowHeight + rowPadding) - rowPadding;
			var height = Math.Min(fixedHeight + listHeight, maxMenuHeight);
			var outer = new Rectangle((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
			var content = new Rectangle(outer.X + contentInset, outer.Y + contentInset, outer.Width - contentInset * 2, outer.Height - contentInset * 2);

			var tableX = content.X + 16;
			var tableWidth = content.Width - 32;
			var toolbarY = outer.Y - ToolbarButtonHeight - 8;
			var title = new Rectangle(content.X, toolbarY - titleHeight - 12, content.Width, titleHeight);
			var seasons = new Rectangle(tableX + 4, toolbarY + 4, SeasonButtonSize * 4 + 8 * 3, SeasonButtonSize);
			var sort = new Rectangle(seasons.Right + 16, toolbarY, Math.Clamp((int)(tableWidth * 0.22f), 180, 260), ToolbarButtonHeight);
			var category = new Rectangle(sort.Right + 12, toolbarY, Math.Clamp((int)(tableWidth * 0.24f), 180, 300), ToolbarButtonHeight);
			var search = new Rectangle(category.Right + 12, toolbarY, Math.Max(tableX + tableWidth - category.Right - 12, 180), ToolbarButtonHeight);
			var header = new Rectangle(tableX, outer.Y + contentTopInset + 10, tableWidth, headerHeight);
			var list = new Rectangle(tableX, header.Bottom, tableWidth, listHeight);

			var priceWidth = Math.Clamp((int)(tableWidth * 0.18f), 150, 210);
			var stockWidth = Math.Clamp((int)(tableWidth * 0.18f), 150, 210);
			var trendWidth = Math.Clamp((int)(tableWidth * 0.14f), 116, 160);
			var itemWidth = Math.Max(tableWidth - priceWidth - stockWidth - trendWidth, 340);
			if (itemWidth + priceWidth + stockWidth + trendWidth > tableWidth)
			{
				trendWidth = Math.Max(tableWidth - itemWidth - priceWidth - stockWidth, 100);
			}

			var item = new Rectangle(tableX, header.Y, itemWidth, list.Bottom - header.Y);
			var price = new Rectangle(item.Right, header.Y, priceWidth, list.Bottom - header.Y);
			var stock = new Rectangle(price.Right, header.Y, stockWidth, list.Bottom - header.Y);
			var trend = new Rectangle(stock.Right, header.Y, tableX + tableWidth - stock.Right, list.Bottom - header.Y);
			var close = new Rectangle(outer.Right + 12, outer.Y - 8, 48, 48);
			var up = new Rectangle(outer.Right + 14, list.Y, 44, 48);
			var down = new Rectangle(outer.Right + 14, list.Bottom - 48, 44, 48);
			var scrollbar = new Rectangle(up.X + 12, up.Bottom + 4, 24, 40);
			var runner = new Rectangle(scrollbar.X, up.Bottom + 4, scrollbar.Width, Math.Max(down.Y - up.Bottom - 8, 40));

			return new ForecastMenuLayout(
				outer,
				title,
				close,
				category,
				sort,
				search,
				seasons,
				header,
				list,
				item,
				price,
				stock,
				trend,
				up,
				down,
				scrollbar,
				runner,
				toolbarY,
				toolbarY,
				rowHeight,
				rowPadding
			);
		}
	}

	private delegate void DrawBoxDelegate(SpriteBatch batch, Rectangle bounds, out Vector2 innerDrawPosition);

	private sealed class ForecastDropdown<TValue> : ClickableComponent
	{
		private const int MaxVisibleItems = 8;
		private readonly TValue[] _items;
		private readonly Func<TValue, string> _getLabel;
		private readonly int _componentBaseId;
		private readonly List<ClickableComponent> _components = [];

		public ForecastDropdown(Rectangle bounds, TValue selected, TValue[] items, Func<TValue, string> getLabel, int componentBaseId)
			: base(bounds, getLabel(selected))
		{
			_items = items;
			_getLabel = getLabel;
			_componentBaseId = componentBaseId;
			Selected = selected;
			myID = CategoryComponentId;
		}

		public bool IsExpanded { get; set; }

		public TValue Selected { get; set; }

		public Rectangle Bounds
		{
			get => bounds;
			set => bounds = value;
		}

		public bool TryClick(int x, int y, out bool itemClicked, out bool dropdownToggled)
		{
			itemClicked = false;
			dropdownToggled = false;

			if (IsExpanded)
			{
				foreach (var component in GetChildComponents())
				{
					if (!component.bounds.Contains(x, y))
					{
						continue;
					}

					if (int.TryParse(component.name, out var index) && index >= 0 && index < _items.Length)
					{
						Selected = _items[index];
						label = _getLabel(Selected);
						itemClicked = true;
						IsExpanded = false;
						dropdownToggled = true;
						return true;
					}
				}
			}

			if (bounds.Contains(x, y) || IsExpanded)
			{
				IsExpanded = !IsExpanded;
				dropdownToggled = true;
				return true;
			}

			return false;
		}

		public bool TrySelectByComponentId(int componentId)
		{
			var index = componentId - _componentBaseId;
			if (index < 0 || index >= _items.Length)
			{
				return false;
			}

			Selected = _items[index];
			label = _getLabel(Selected);
			return true;
		}

		public bool ContainsComponentId(int componentId)
		{
			var visibleCount = GetVisibleCount();
			return componentId >= _componentBaseId && componentId < _componentBaseId + visibleCount;
		}

		public IEnumerable<ClickableComponent> GetChildComponents()
		{
			_components.Clear();
			var visibleCount = GetVisibleCount();
			for (var i = 0; i < visibleCount; i++)
			{
				var itemIndex = i;
				var component = new ClickableComponent(GetItemBounds(i), itemIndex.ToString(), _getLabel(_items[itemIndex]))
				{
					myID = _componentBaseId + i,
					upNeighborID = i == 0 ? CategoryComponentId : _componentBaseId + i - 1,
					downNeighborID = i == visibleCount - 1 ? UpArrowComponentId : _componentBaseId + i + 1,
				};
				_components.Add(component);
			}

			return _components;
		}

		public void Draw(SpriteBatch batch, DrawBoxDelegate drawBox, bool drawItems = true)
		{
			label = _getLabel(Selected);
			drawBox(batch, bounds, out var textPos);
			if (Game1.smallFont != null)
			{
				batch.DrawString(Game1.smallFont, label, textPos, Game1.textColor);
			}
			if (Game1.mouseCursors != null)
			{
				batch.Draw(Game1.mouseCursors, new Vector2(bounds.Right - 34, bounds.Y + 12), new Rectangle(437, 450, 10, 11), Color.White, 0, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 1f);
			}

			if (!IsExpanded || !drawItems)
			{
				return;
			}

			DrawItems(batch, drawBox);
		}

		public void DrawItems(SpriteBatch batch, DrawBoxDelegate drawBox)
		{
			var visibleCount = GetVisibleCount();
			if (visibleCount <= 0)
			{
				return;
			}

			var popupBounds = new Rectangle(bounds.X, bounds.Bottom - 2, Math.Max(bounds.Width, 220), visibleCount * bounds.Height + 16);
			if (Game1.menuTexture != null)
			{
				IClickableMenu.drawTextureBox(batch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), popupBounds.X, popupBounds.Y, popupBounds.Width, popupBounds.Height, Color.White);
			}

			foreach (var component in GetChildComponents())
			{
				var selected = EqualityComparer<TValue>.Default.Equals(_items[int.Parse(component.name)], Selected);
				if (selected)
				{
					DrawFilledRect(batch, new Rectangle(component.bounds.X + 10, component.bounds.Y + 5, component.bounds.Width - 20, component.bounds.Height - 10), new Color(255, 230, 160) * 0.72f);
				}

				if (Game1.smallFont != null)
				{
					batch.DrawString(Game1.smallFont, component.label, new Vector2(component.bounds.X + 16, component.bounds.Y + 9), Game1.textColor);
				}

				DrawFilledRect(batch, new Rectangle(component.bounds.X + 10, component.bounds.Bottom - 1, component.bounds.Width - 20, 1), new Color(132, 94, 48) * 0.7f);
			}
		}

		private int GetVisibleCount() => Math.Min(MaxVisibleItems, _items.Length);

		private Rectangle GetItemBounds(int visibleIndex)
		{
			return new Rectangle(bounds.X, bounds.Bottom + visibleIndex * bounds.Height, Math.Max(bounds.Width, 220), bounds.Height);
		}
	}

	private readonly record struct FilterTab(
		int Id,
		string Label,
		Rectangle IconSource,
		Rectangle Bounds,
		bool Selected,
		bool Enabled
	);

	private void SetupItemsWithSort()
	{
		List<ItemModel> items;

		if (_chosenCategory == int.MinValue)
		{
			items = _economyService.GetCategories()
				.SelectMany(c => _economyService.GetItemsForCategory(c.Key))
				.ToList();
		}
		else
		{
			items = _economyService.GetItemsForCategory(_chosenCategory).ToList();
		}

		var searchText = _textFilter.Trim();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			items = items.Where(item =>
			{
				var obj = item.GetObjectInstance(1);
				return obj.Name.Contains(searchText, StringComparison.InvariantCultureIgnoreCase)
					|| obj.DisplayName.Contains(searchText, StringComparison.InvariantCultureIgnoreCase);
			}).ToList();
		}

		switch (_chosenSort)
		{
			case nameof(Name):
			{
				items.Sort((a, b) =>
					string.Compare(a.GetObjectInstance(1).DisplayName, b.GetObjectInstance(1).DisplayName, StringComparison.Ordinal)
				);
				break;
			}
			case nameof(Supply):
			{
				items.Sort((a, b) => _economyService.GetConsolidatedItem(a).Supply - _economyService.GetConsolidatedItem(b).Supply);
				break;
			}
			case nameof(DailyChange):
			{
				items.Sort((a, b) => _economyService.GetConsolidatedItem(a).DailyDelta - _economyService.GetConsolidatedItem(b).DailyDelta);
				break;
			}
			case nameof(MarketPrice):
			{
				items.Sort((a, b) =>
				{
					var aObj = a.GetObjectInstance(1);
					var bObj = b.GetObjectInstance(1);
					return _economyService.GetPrice(bObj, bObj.sellToStorePrice()) - _economyService.GetPrice(aObj, aObj.sellToStorePrice());
				});
				break;
			}
		}

		items = items.Where(i => _economyService.ItemValidForSeason(i, _chosenSeasons)).DistinctBy(i => i.ObjectId).ToList();

		_allItems = items.ToArray();
	}
}
