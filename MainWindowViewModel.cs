using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snake.Core.DataModels;
using Snake.Core.Models;
using Snake.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace WPF_Snake.ViewModels;

/// <summary>
/// Модель представления главного окна для главного окна
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    #region Поля

    private Window _window;

    private Random _random = new Random();

    private const int MAX_GAME_GRID_SIZE = 400;
    private const int MAX_GAME_GRID_ROWS = 40;
    private const int MAX_GAME_GRID_COLUMNS = 40;
    private const string HIGH_SCORES_PATH = "Resources/HighScores.json";

    private Direction _currentDirection = Direction.DOWN;
    private Queue<NextMove> _nextMoves = new Queue<NextMove>();
    private object _lock = new object();
    private int _snakeSpeed = 200;

    #endregion

    #region Характеристики

    /// <summary>
    /// Размер игровой сетки
    /// </summary>
    public int GameGridSize => MAX_GAME_GRID_SIZE;

    /// <summary>
    /// Текущий счёт
    /// </summary>
    [ObservableProperty]
    private int score = 0;

    /// <summary>
    /// Флаг, который сообщает что игра окончена.
    /// </summary>
    [ObservableProperty]
    private bool gameOver = true;

    /// <summary>
    /// Флаг, который сообщает, видно ли главное меню.
    /// </summary>
    [ObservableProperty]
    private bool mainMenuVisible = true;

    /// <summary>
    /// Флаг, который сообщает, видно ли очки.
    /// </summary>
    [ObservableProperty]
    private bool highScoresVisible;

    /// <summary>
    /// Фрукты растут случайным образом, и растёт змея.
    /// </summary>
    [ObservableProperty]
    private CellViewModel fruit;

    /// <summary>
    /// Змея, которую нужно нарисовать в игровой сетке
    /// </summary>
    public ObservableCollection<CellViewModel> Snake { get; set; } = new ObservableCollection<CellViewModel>();

    /// <summary>
    /// Очки
    /// </summary>
    [ObservableProperty]
    public ObservableCollection<HighScoreViewModel> highScores = new ObservableCollection<HighScoreViewModel>();

    #endregion

    #region Конструктор

    public MainWindowViewModel(Window window)
    {
        _window = window;
        _window.KeyUp += _window_KeyUp;

        BindingOperations.EnableCollectionSynchronization(Snake, _lock);
        BindingOperations.EnableCollectionSynchronization(HighScores, _lock);
    }

    #endregion

    #region Методы команд

    /// <summary>
    /// Показать очки
    /// </summary>
    [RelayCommand]
    private void ShowHighScores()
    {
        HighScoresVisible = true;
        MainMenuVisible = false;

        var hs = LoadHighScores();
        HighScores = new ObservableCollection<HighScoreViewModel>(hs);
    }

    /// <summary>
    /// Спрятать окно с очками и отобразить окно главного меню
    /// </summary>
    [RelayCommand]
    private void ShowMainMenu()
    {
        HighScoresVisible = false;
        MainMenuVisible = true;

        var jsonString = JsonSerializer.Serialize(HighScores, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(HIGH_SCORES_PATH, jsonString);
    }

    /// <summary>
    /// Спрятать главное меню и начать игру
    /// </summary>
    [RelayCommand]
    private void Play()
    {
        CreateSnake();
        SpawnFruit();
        MainMenuVisible = false;
        GameOver = false;
        Score = 0;
        Task.Run(() => GameLoop());
    }

    #endregion

    #region Методы

    private List<HighScoreViewModel> LoadHighScores(bool withZeros = false)
    {
        var highScores = new List<HighScoreViewModel>();
        var jsonString = File.ReadAllText(HIGH_SCORES_PATH);
        if (!string.IsNullOrEmpty(jsonString))
            highScores = JsonSerializer.Deserialize<List<HighScoreViewModel>>(jsonString);

        if (withZeros)
        {
            while (highScores.Count < 10)
            {
                var hs = new HighScoreViewModel();
                hs.Score = 0;
                hs.Name = String.Empty;
                highScores.Add(hs);
            }
        }
        return highScores;
    }

    private void CreateSnake()
    {
        Snake.Clear();
        var snakeHead = new CellViewModel(200, 200);
        snakeHead.Rgb = CellViewModel.SNAKE_HEAD_RGB;
        Snake.Add(snakeHead);
    }

    /// <summary>
    /// Игра
    /// </summary>
    private void GameLoop()
    {
        while (!GameOver)
        {
            Thread.Sleep(_snakeSpeed);

            if (_nextMoves.Any())
            {
                var move = _nextMoves.Dequeue();
                _currentDirection = move.Direction;
                CheckIfSnakeEatSelf(move.Xpos, move.Ypos);
                CheckIfSnakeHitWall(move.Xpos, move.Ypos);
                if (!GameOver)
                    MoveSnake(move.Xpos, move.Ypos);
            }
            else
                MoveSnake();

            CheckIfFruitEaten();
        }

        var highScores = LoadHighScores(true);
        var hs = new HighScoreViewModel();
        hs.Score = Score;
        hs.IsOldScore = false;
        hs.Focus = true;

        if (Score > highScores.Last().Score)
        {
            if (Score > highScores.First().Score)
                highScores.Insert(0, hs);
            else
            {
                bool scoreAdded = false;
                for (int i = highScores.Count - 1; i >= 0; i--)
                {
                    if (Score <= highScores[i].Score)
                    {
                        highScores.Insert(i + 1, hs);
                        scoreAdded = true;
                        break;
                    }
                }

                if (!scoreAdded)
                    highScores.Add(hs);
            }

            if (highScores.Count > 10)
                highScores.RemoveAt(10);

            while (highScores.Last().Score == 0)
            {
                highScores.Remove(highScores.Last());
            }
        }

        HighScores = new ObservableCollection<HighScoreViewModel>(highScores);
        HighScoresVisible = true;
    }

    /// <summary>
    /// Событие нажатия клавиши окна уведомляет нас о том, что клавиша была отпущена.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void _window_KeyUp(object sender, KeyEventArgs e)
    {
        // Проверяем, нажаты ли клавиши не из набора WASD и стрелок
        if (e.Key != Key.Left && e.Key != Key.Up && e.Key != Key.Right && e.Key != Key.Down && e.Key != Key.W && e.Key != Key.A && e.Key != Key.S && e.Key != Key.D)
            return;        

        int xPos = 0;
        int yPos = 0;
        Direction newDirecton = Direction.LEFT;

        switch (e.Key)
        {
            case Key.Left:
                if (_currentDirection == Direction.RIGHT && Snake.Count > 1)
                    return;
                xPos -= CellViewModel.CELL_SIZE;
                newDirecton = Direction.LEFT;
                break;
            case Key.Up:
                if (_currentDirection == Direction.DOWN && Snake.Count > 1)
                    return;
                yPos -= CellViewModel.CELL_SIZE;
                newDirecton = Direction.UP;
                break;
            case Key.Right:
                if (_currentDirection == Direction.LEFT && Snake.Count > 1)
                    return;
                xPos += CellViewModel.CELL_SIZE;
                newDirecton = Direction.RIGHT;
                break;
            case Key.Down:
                if (_currentDirection == Direction.UP && Snake.Count > 1)
                    return;
                yPos += CellViewModel.CELL_SIZE;
                newDirecton = Direction.DOWN;
                break;
        }

        switch (e.Key)
        {
            case Key.A:
                if (_currentDirection == Direction.RIGHT && Snake.Count > 1)
                    return;
                xPos -= CellViewModel.CELL_SIZE;
                newDirecton = Direction.LEFT;
                break;
            case Key.W:
                if (_currentDirection == Direction.DOWN && Snake.Count > 1)
                    return;
                yPos -= CellViewModel.CELL_SIZE;
                newDirecton = Direction.UP;
                break;
            case Key.D:
                if (_currentDirection == Direction.LEFT && Snake.Count > 1)
                    return;
                xPos += CellViewModel.CELL_SIZE;
                newDirecton = Direction.RIGHT;
                break;
            case Key.S:
                if (_currentDirection == Direction.UP && Snake.Count > 1)
                    return;
                yPos += CellViewModel.CELL_SIZE;
                newDirecton = Direction.DOWN;
                break;
        }
        _nextMoves.Enqueue(new NextMove(xPos, yPos, newDirecton));
    }

    /// <summary>
    /// Проверяет, ударилась ли змея о стену
    /// </summary>
    /// <param name="xPos"></param>
    /// <param name="yPos"></param>
    private void CheckIfSnakeHitWall(int xPos, int yPos)
    {
        var nextHeadPositionX = Snake.Last().XPos + xPos;
        var nextHeadPositionY = Snake.Last().YPos + yPos;

        if(nextHeadPositionX < 0 || nextHeadPositionY < 0 || nextHeadPositionX >= MAX_GAME_GRID_SIZE || nextHeadPositionY >= MAX_GAME_GRID_SIZE)
           GameOver = true;
    }

    /// <summary>
    /// Проверяет, съела ли змея себя
    /// </summary>
    /// <param name="xPos"></param>
    /// <param name="yPos"></param>
    private void CheckIfSnakeEatSelf(int xPos, int yPos)
    {
        var nextHeadPositionX = Snake.Last().XPos + xPos;
        var nextHeadPositionY = Snake.Last().YPos + yPos;

        for (int index = 1; index < Snake.Count - 1; index++)
        {
            if (Snake[index].YPos.Equals(nextHeadPositionY) && Snake[index].XPos.Equals(nextHeadPositionX))
                GameOver = true;
        }
    }

    /// <summary>
    /// Проверяет, съела ли змея фрукт. Если да, то появится новый плод и змея вырастет.
    /// </summary>
    private void CheckIfFruitEaten()
    {
        var snakeX = Snake.Last().XPos;
        var snakeY = Snake.Last().YPos;
        if (snakeX.Equals(Fruit.XPos) && snakeY.Equals(Fruit.YPos))
        {
            GrowSnake();
            SpawnFruit();
            Score++;
            _snakeSpeed -= 2;
        }
    }

    /// <summary>
    /// Перемещает змею на один шаг в текущем направлении.
    /// </summary>
    private void MoveSnake()
    {
        int xPos = 0;
        int yPos = 0;

        switch (_currentDirection)
        {
            case Direction.LEFT:
                xPos -= CellViewModel.CELL_SIZE;
                break;
            case Direction.UP:
                yPos -= CellViewModel.CELL_SIZE;
                break;
            case Direction.RIGHT:
                xPos += CellViewModel.CELL_SIZE;
                break;
            case Direction.DOWN:
                yPos += CellViewModel.CELL_SIZE;
                break;
        }

        CheckIfSnakeEatSelf(xPos, yPos);
        CheckIfSnakeHitWall(xPos, yPos);
        if (!GameOver)
            MoveSnake(xPos, yPos);
    }

    /// <summary>
    /// Перемещает змею в следующую позицию
    /// </summary>
    /// <param name="xPos"></param>
    /// <param name="yPos"></param>
    private void MoveSnake(int xPos, int yPos)
    {
        for(int index = 0; index < Snake.Count-1; index++)
        {
            Snake[index].XPos = Snake[index + 1].XPos;
            Snake[index].YPos = Snake[index + 1].YPos;
        }

        int newX = Snake.Last().XPos + xPos;
        int newY = Snake.Last().YPos + yPos;

        if(newX < 0)
            newX = MAX_GAME_GRID_SIZE-10;
        else if(newX > MAX_GAME_GRID_SIZE-10)
            newX = 0;

        if (newY < 0)
            newY = MAX_GAME_GRID_SIZE-10;
        else if (newY > MAX_GAME_GRID_SIZE-10)
            newY = 0;

        Snake.Last().XPos = newX;
        Snake.Last().YPos = newY;
    }

    /// <summary>
    /// Выращивает змею
    /// </summary>
    private void GrowSnake()
    {
        var snakeSection = new CellViewModel(Snake.First().XPos, Snake.First().YPos);
        if (Snake.First().Rgb.Equals(CellViewModel.SNAKE_HEAD_RGB))
            snakeSection.Rgb = CellViewModel.SNAKE_BODY1_RGB;
        else if (Snake.First().Rgb.Equals(CellViewModel.SNAKE_BODY1_RGB))
            snakeSection.Rgb = CellViewModel.SNAKE_BODY2_RGB;
        else if (Snake.First().Rgb.Equals(CellViewModel.SNAKE_BODY2_RGB))
            snakeSection.Rgb = CellViewModel.SNAKE_BODY3_RGB;
        else
            snakeSection.Rgb = CellViewModel.SNAKE_BODY1_RGB;

        Snake.Insert(0, snakeSection);
    }

    /// <summary>
    /// Генерирует новый фрукт в случайном месте
    /// </summary>
    private void SpawnFruit()
    {
        bool foundSection = false;
        int xPos = 0;
        int yPos = 0;
        do
        {
            xPos = _random.Next(0, MAX_GAME_GRID_ROWS) * 10;
            yPos = _random.Next(0, MAX_GAME_GRID_COLUMNS) * 10;
            foundSection = Snake.FirstOrDefault(item => item.XPos.Equals(xPos) && item.YPos.Equals(yPos)) != null;
        } while (foundSection);

        Fruit = new CellViewModel(xPos, yPos);
    }
    #endregion
}