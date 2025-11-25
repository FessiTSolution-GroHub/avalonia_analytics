using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using OxyPlot;
using OxyPlot.Series;
using SkiaSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;//(Language Integrated Query)
using System.Threading.Tasks;
using System.Timers;
using User_Analytics.Models;

// Alias(nicknames for long type names)
using LCAxis = LiveChartsCore.SkiaSharpView.Axis;
using PieSeries = OxyPlot.Series.PieSeries;

namespace User_Analytics.ViewModels
{ 
    public partial class MainWindowViewModel : ViewModelBase//inherits functionality from ViewModelBase
    {
        [ObservableProperty] private int totalUsers;//automatically generates a public property that notifies the UI whenever its value changes
        [ObservableProperty] private int activeUsers;
        [ObservableProperty] private int departmentCount;
        [ObservableProperty] private string selectedYearLabel = string.Empty;

        public ObservableCollection<UserDisplayModel> Users { get; } = new();// users(collection), automatically notifies the UI for updates

        // Chart related fields and properties
        private ISeries[] _registrationSeries = Array.Empty<ISeries>();//ISeries (array of the chart series)
        public ISeries[] RegistrationSeries//exposes the data safely to the rest of the app
        {
            get => _registrationSeries;
            private set => SetProperty(ref _registrationSeries, value);
        }

        private LCAxis[] _registrationXAxes = Array.Empty<LCAxis>();//A private field that holds an array of X-axis objects for the chart.
        public LCAxis[] RegistrationXAxes//A public property that exposes the X-axis data to the rest of the app, mainly for UI binding.
        {
            get => _registrationXAxes;
            private set => SetProperty(ref _registrationXAxes, value);
        }

        private LCAxis[] _registrationYAxes = Array.Empty<LCAxis>();
        public LCAxis[] RegistrationYAxes
        {
            get => _registrationYAxes;
            private set => SetProperty(ref _registrationYAxes, value);
        }

        private ISeries[] _departmentPieChart = Array.Empty<ISeries>();
        public ISeries[] DepartmentPieChart
        {
            get => _departmentPieChart;
            private set => SetProperty(ref _departmentPieChart, value);
        }

        private PlotModel _targetAchievementChart = new PlotModel();
        public PlotModel TargetAchievementChart
        {
            get => _targetAchievementChart;
            set => SetProperty(ref _targetAchievementChart, value);
        }
        public ObservableCollection<ISeries> MonthlySeries { get; } = new();
        public LCAxis[] MonthlyXAxes { get; set; } = Array.Empty<LCAxis>();
        public LCAxis[] MonthlyYAxes { get; set; } = Array.Empty<LCAxis>();

        public ObservableCollection<IPolarSeries> TargetAchievementSeries { get; } = new();
        public ObservableCollection<TargetAchievementLegendItem> TargetAchievementLegend { get; } = new();
        
        private List<MongoDatabaseService.UserModel> _cachedUsers = new();
        private bool _isYearManualSelection = false;

        private int _currentYearIndex;//Tracks the year we're currently viewing 
        private int[] _availableYears = Array.Empty<int>();//An array of years we have data for.
        private readonly string[] _months = System.Globalization.CultureInfo.CurrentCulture// gets the user's regional settings (like "English - United States")
       .DateTimeFormat.MonthNames//access the date and time format of that culture
       .Take(12)
       .ToArray();

        private readonly Dictionary<string, int> _departmentTargets = new()//Maps the Department Name and te Target count, so the database knows the number of users to generate per department
        {
            { "Software Engineer", 3500 }, 
            { "Firmware Engineer", 3500 },
            { "IT", 3000 },
            { "Mechanical Engineer", 2000 },
            { "Marketing", 1000 },
            { "HR", 700 }
        };
        private readonly MongoDatabaseService _dbService;//holds the instance of the MongoDatabaseService Method from Models

        private readonly Timer _liveUpdateTimer;//Updates the users every few second 
        public MainWindowViewModel()
        {
            // MongoDB connection
            _dbService = new MongoDatabaseService(
                "mongodb://localhost:27017",//URL
                "UserAnalyticsDb",//Database name
                "Users"//Collection
            );

            // Clear users for fresh start
            Task.Run(async () =>
            {
                await _dbService.ClearAllUsersAsync(); // start from 0
                await RefreshDataAsync(); // initial load
            });

            // Start faster live update timer (insert multiple users every 2 sec)
            _liveUpdateTimer = new Timer(2000);
            _liveUpdateTimer.Elapsed += async (s, e) =>//Event Handler (defined but Unused)
            {
                int insertedCount = await InsertRandomUsersAsync(46);

                // If no users were inserted, all departments have reached their targets
                if (insertedCount == 0)
                { 
                    _liveUpdateTimer.Stop();//stops once reached the target
                }
                await RefreshDataAsync();
            };
            _liveUpdateTimer.Start();

            MonthlyXAxes = new[]// X Axes
            {
    new LCAxis
    {
        Labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" },
        MinStep = 1,
        ForceStepToMin = true,
        LabelsRotation = 0
    }
            };

            MonthlyYAxes = new[]// Y Axes
            {
    new LCAxis
    {
        Name = "Registrations",
        MinLimit = 0
    }
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => StopTimer();//Event that's fired When the application is shutting down to safely stop te timer
        }
        public void StopTimer()
        {
            _liveUpdateTimer?.Stop();
            _liveUpdateTimer?.Dispose();//releases the timer's resources back to the system.
        }

        private async Task<int> InsertRandomUsersAsync(int count)
        {
            var currentCounts = _cachedUsers            // Get current counts per department from cached data
                .Where(u => u.IsActive)
                .GroupBy(u => string.IsNullOrWhiteSpace(u.Department) ? "Other" : u.Department)
                .ToDictionary(g => g.Key, g => g.Count());//Converts the grouped result into a Dictionary

            // Find departments that still need users (haven't reached target)
            var availableDepartments = _departmentTargets
                .Where(kvp => !currentCounts.ContainsKey(kvp.Key) || currentCounts[kvp.Key] < kvp.Value)
                .Select(kvp => kvp.Key)
                .ToArray(); 
            
            // If no departments need users, return 0 (all targets met)
            if (availableDepartments.Length == 0)
                return 0;

            var random = new Random();//another random Object

            int insertedCount = 0;//Start at 0
            for (int i = 0; i < count; i++)//Each time we successfully insert a new user, we’ll increase this counter by 1.
            {
                // Pick random department from available ones from 0 - 5
                string selectedDept = availableDepartments[random.Next(availableDepartments.Length)];

                // Get current count for this department
                int currentCount = currentCounts.ContainsKey(selectedDept)
                    ? currentCounts[selectedDept]
                    : 0;
                
                int target = _departmentTargets[selectedDept];

                // Check if this department still needs users
                if (currentCount < target)
                {
                    // Call the service method to insert user for specific department
                    await _dbService.InsertRandomUserForDepartmentAsync(selectedDept);
                    insertedCount++;

                    // Update local count so we know about this insert
                    currentCounts[selectedDept] = currentCount + 1;

                    // Recheck available departments (in case we filled just one)
                    availableDepartments = _departmentTargets
                        .Where(kvp => !currentCounts.ContainsKey(kvp.Key) || currentCounts[kvp.Key] < kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToArray();

                    // If no more available departments, stop inserting
                    if (availableDepartments.Length == 0)
                        break;
                }
            }

            return insertedCount;
        }

        private async Task RefreshDataAsync()
        {
            _cachedUsers = await _dbService.GetAllUsersAsync();

            Users.Clear();//Clear the observable collection that the UI is bound to.
            foreach (var u in _cachedUsers)
            {
                Users.Add(new UserDisplayModel//This creates a new object of the UserDisplayModel class
                {
                    Id = u.Id,
                    Name = u.Name,
                    Department = u.Department,
                    ExperienceLevel = u.ExperienceLevel,
                    RegistrationDate = u.RegistrationDate.ToString("yyyy-MM-dd"),
                    IsActive = u.IsActive
                });
            }

            TotalUsers = _cachedUsers.Count;
            ActiveUsers = _cachedUsers.Count(u => u.IsActive);
            DepartmentCount = _cachedUsers.Select(u => u.Department).Distinct().Count();

            // Set available years **only once** if not manually selected
            if (!_isYearManualSelection) 
            {
                _availableYears = Enumerable.Range(2018, 8).ToArray(); // 2019–2025 fixed
                _currentYearIndex = _availableYears.Length - 1; // start at 2025
                SelectedYearLabel = $"{_availableYears[_currentYearIndex]} Registration Growth";
            }

            // Load charts
            LoadDepartmentChart(_cachedUsers);    
            LoadRegistrationChart(_cachedUsers);
            LoadMonthlyGrowthChart(_cachedUsers, _availableYears[_currentYearIndex]);
            LoadTargetAchievementGauge(_cachedUsers);
        }

        #region Charts
        private void LoadRegistrationChart(List<MongoDatabaseService.UserModel> allUsers)
        {
            var activeUsers = allUsers.Where(u => u.IsActive);
            var usersPerYear = activeUsers
                .GroupBy(u => u.RegistrationDate.Year)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());

            var years = usersPerYear.Keys.ToArray();
            var counts = usersPerYear.Values.ToArray();

            // <--- Row chart--->
            if (RegistrationSeries.Length == 0)
            {
                RegistrationSeries = new ISeries[]
                {
            new RowSeries<int>
            {
                Values = counts,
                Fill = new SolidColorPaint(new SKColor(103, 80, 164)),
                Stroke = new SolidColorPaint(new SKColor(103, 80, 164, 255)) { StrokeThickness = 2 },
                MaxBarWidth = 50,
                Name = "Annual User Growth",
                DataLabelsSize = 10,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
                Rx = 6,
                Ry = 2
            }
                };
            }
            else
            {
                (RegistrationSeries[0] as RowSeries<int>)!.Values = counts;
            }

            _registrationXAxes = new[]
            {
        new LCAxis
        {
            MinLimit = 0,
            LabelsPaint = new SolidColorPaint(SKColors.Black)
            {
                SKTypeface = SKTypeface.FromFamilyName("Arial"),
            },
           TextSize = 12
        }
    };

            _registrationYAxes = new[]
            {
        new LCAxis
        {
            Labels = years.Select(y => y.ToString()).ToArray(),
            LabelsPaint = new SolidColorPaint(SKColors.Black),
            TextSize = 12,
            ForceStepToMin=true,
            MinStep = 1, // force every year visible
        }
    };
            OnPropertyChanged(nameof(RegistrationXAxes));
            OnPropertyChanged(nameof(RegistrationYAxes));
        }

        private void LoadDepartmentChart(List<MongoDatabaseService.UserModel> allUsers)
        {
            var deptCounts = allUsers.Where(u => u.IsActive)
                                     .GroupBy(u => string.IsNullOrWhiteSpace(u.Department) ? "Other" : u.Department)
                                     .ToDictionary(g => g.Key, g => g.Count());

            Dispatcher.UIThread.Post(() => //Runs the following code on the UI thread, safely
            {
                if (DepartmentPieChart.Length == 0)
                {
                    _departmentPieChart = deptCounts.Select(kvp =>
                        new LiveChartsCore.SkiaSharpView.PieSeries<int>
                        {
                            Name = kvp.Key,
                            Values = new[] { kvp.Value },
                            DataLabelsPaint = new SolidColorPaint(SKColors.White),
                            DataLabelsSize = 14,
                            Fill = new SolidColorPaint(GetColorForDepartment(kvp.Key))
                        } as ISeries).ToArray();
                }
                else
                {
                    foreach ((PieSeries<int> series, string key) in from PieSeries<int> series in DepartmentPieChart.OfType<PieSeries<int>>()
                                                  let key = series.Name ?? string.Empty
                                                  select (series, key))
                    {
                        if (deptCounts.TryGetValue(key, out var value))
                            series.Values = new[] { value };
                        else
                            series.Values = new[] { 0 };
                    }
                }
                OnPropertyChanged(nameof(DepartmentPieChart));
            });
        }

        private void LoadMonthlyGrowthChart(List<MongoDatabaseService.UserModel> allUsers, int year)
        {
            Dispatcher.UIThread.Post(() =>  //Runs the following code on the UI thread, safely
            {
                var usersInYear = allUsers
                    .Where(u => u.RegistrationDate.Year == year)
                    .ToList();

                var departments = usersInYear
                    .Select(u => string.IsNullOrWhiteSpace(u.Department) ? "Other" : u.Department)
                    .Distinct()
                    .ToList();

                var deptMonthly = new Dictionary<string, int[]>();
                foreach (var dept in departments)
                {
                    var monthlyCounts = Enumerable.Range(1, 12)
                        .Select(m => usersInYear.Count(u =>
                            (string.IsNullOrWhiteSpace(u.Department) ? "Other" : u.Department) == dept &&
                            u.RegistrationDate.Month == m))
                        .ToArray();
                    deptMonthly[dept] = monthlyCounts;
                }

                SKColor GetDeptColor(string dept) => dept.ToLower() switch
                {
                    "software engineer" => new SKColor(103, 80, 164),
                    "firmware engineer" => new SKColor(52, 152, 219),
                    "mechanical engineer" => new SKColor(46, 204, 113),
                    "it" => new SKColor(231, 76, 60),
                    "marketing" => new SKColor(241, 196, 15),
                    "hr" => new SKColor(155, 89, 182),
                    _ => new SKColor(149, 165, 166)
                };

                if (MonthlySeries.Count == 0)//if no  series yet , then it builds the chart series.
                {
                    foreach (var kvp in deptMonthly)
                    {
                        MonthlySeries.Add(new LineSeries<int>
                        {
                            Name = kvp.Key,
                            Values = kvp.Value,
                            Fill = null,
                            Stroke = new SolidColorPaint(GetDeptColor(kvp.Key))
                            {
                                StrokeThickness = 1.5f // thinner line
                            },
                            GeometrySize = 4, // small pointer size
                            GeometryStroke = new SolidColorPaint(GetDeptColor(kvp.Key)),
                            GeometryFill = new SolidColorPaint(GetDeptColor(kvp.Key)),
                            DataLabelsSize = 10,
                            DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top
                        });
                    }
                }
                else
                {
                    foreach (var series in MonthlySeries.OfType<LineSeries<int>>())//if already exists, update it with fresh values
                    {
                        var key = series.Name ?? string.Empty;
                        if (deptMonthly.TryGetValue(key, out var newValues))
                        {
                            series.Values = newValues;
                            series.Stroke = new SolidColorPaint(GetDeptColor(key))
                            {
                                StrokeThickness = 1.5f
                            };
                            series.GeometrySize =4;
                            series.GeometryStroke = new SolidColorPaint(GetDeptColor(key));
                            series.GeometryFill = new SolidColorPaint(GetDeptColor(key));
                        }
                        else
                        {
                            series.Values = new int[12];
                        }
                    }

                    foreach (var dept in deptMonthly.Keys.Except(MonthlySeries.Select(s => s.Name ?? string.Empty)))//finds new values and updtes new lines 
                    {
                        MonthlySeries.Add(new LineSeries<int>
                        {
                            Name = dept,
                            Values = deptMonthly[dept],
                            Fill = null,
                            Stroke = new SolidColorPaint(GetDeptColor(dept))
                            {
                                StrokeThickness = 1.5f
                            },
                            GeometrySize = 4,
                            GeometryStroke = new SolidColorPaint(GetDeptColor(dept)),
                            GeometryFill = new SolidColorPaint(GetDeptColor(dept)),
                            DataLabelsSize = 10,
                            DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top
                        });
                    }
                }
            });
        }

        [RelayCommand]//Binded to the buttons on the UI layer
        private void PreviousYear()
        {
            if (_currentYearIndex > 0)
            {
                _currentYearIndex--;
                _isYearManualSelection = true;
                SelectedYearLabel = $"{_availableYears[_currentYearIndex]} Registration Growth";
                LoadMonthlyGrowthChart(_cachedUsers, _availableYears[_currentYearIndex]);
            }
        }

        [RelayCommand]
        private void NextYear()
        { 
            if (_currentYearIndex < _availableYears.Length - 1)
            {
                _currentYearIndex++;
                _isYearManualSelection = true;
                SelectedYearLabel = $"{_availableYears[_currentYearIndex]} Registration Growth";
                LoadMonthlyGrowthChart(_cachedUsers, _availableYears[_currentYearIndex]);
            }
        }

        private void LoadTargetAchievementGauge(List<MongoDatabaseService.UserModel> allUsers)
        {
            Dispatcher.UIThread.Post(() =>  //Runs the following code on the UI thread, safely
            {
                TargetAchievementLegend.Clear();//Clears the old data from the Legend 
                var model = new PlotModel//creating a new chart layout(For OxyPlot).
                {
                    Background = OxyColors.Transparent,
                    PlotMargins = new OxyThickness(0, 0, 250, 0), // Removed the 250 right margin
                    PlotAreaBorderThickness = new OxyThickness(0),
                    Padding = new OxyThickness(10),
                    IsLegendVisible = false
                };

                double bandThickness = 0.12;
                double gap = 0.02;
                double outerMostDiameter = 1.0;
                int deptIndex = 0;

                foreach (var dept in _departmentTargets.Keys.Where(d => d.ToLower() != "other"))
                {
                    int actual = allUsers.Count(u =>
                        (string.IsNullOrWhiteSpace(u.Department) ? "Other" : u.Department) == dept && u.IsActive);
                    int target = _departmentTargets[dept];
                    double percent = Math.Min(100.0 * actual / Math.Max(1, target), 100.0);//Calculates the percentage progress toward the target:
                    double outer = outerMostDiameter - deptIndex * (bandThickness + gap);
                    double inner = Math.Max(0.0, outer - bandThickness);
                    outer = Math.Max(0.0, Math.Min(1.0, outer));//avoids dividing by zero (in case the target is accidentally 0).
                    inner = Math.Max(0.0, Math.Min(outer, inner));//caps it at 100%, so it never exceeds full progress.

                    var pie = new PieSeries
                    {
                        StartAngle = 180,
                        AngleSpan = 360,
                        InnerDiameter = inner,
                        Diameter = outer,
                        StrokeThickness = 0,
                        OutsideLabelFormat = string.Empty,
                        InsideLabelFormat = string.Empty,
                        TickHorizontalLength = 0,
                        TickRadialLength = 0,
                        FontSize = 0,
                    };

                    var achievedSlice = new PieSlice(dept, percent)//Creates a new colored slice of the pie chart
                    {
                        Fill = ToOxyColor(GetColorForDepartment(dept)),
                        IsExploded = false
                    };

                    var remainder = new PieSlice(string.Empty, 100 - percent)//Creates another slice for the remaining (unfilled) portion.
                    {
                        Fill = OxyColor.FromAColor(40, OxyColors.Gray),
                        IsExploded = false
                    };

                    pie.Slices.Add(achievedSlice);
                    pie.Slices.Add(remainder);
                    model.Series.Add(pie);//Adds both slices (colored + gray remainder) to a single PieSeries.

                    TargetAchievementLegend.Add(new TargetAchievementLegendItem
                    {
                        Department = dept,
                        ValueText = $"{actual}/{target} ({percent:0}%)",
                        Color = ConvertSkColorToAvalonia(GetColorForDepartment(dept))
                    });

                    deptIndex++;
                }

                TargetAchievementChart = model;
            });
        }
        private OxyColor ToOxyColor(SKColor c)//converts a color from SkiaSharp’s format (SKColor) into OxyPlot’s format(OxyColor).
        {
            return OxyColor.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
        }
        private Avalonia.Media.Color ConvertSkColorToAvalonia(SKColor skColor)//Converts from SkiaSharp’s SKColor into Avalonia’s Color format.
        {
            return Avalonia.Media.Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
        }

        private SKColor GetColorForDepartment(string dept)
        {
            return dept.ToLower() switch
            {
                "software engineer" => new SKColor(103, 80, 164),
                "firmware engineer" => new SKColor(52, 152, 219),
                "mechanical engineer" => new SKColor(46, 204, 113),
                "it" => new SKColor(231, 76, 60),
                "marketing" => new SKColor(241, 196, 15),
                "hr" => new SKColor(155, 89, 182),
                _ => new SKColor(149, 165, 166)
            };
        }
        #endregion
        //model classes
        public class TargetAchievementLegendItem//Represents a single legend entry for your Target vs Achievement chart.
        {
            public string Department { get; set; } = string.Empty;
            public string ValueText { get; set; } = string.Empty;
            public Avalonia.Media.Color Color { get; set; }
            public ObservableCollection<ISeries> DepartmentSeries { get; } = new();
        }

        public record UserDisplayModel//model representing a user’s display data in the analytics UI.
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Department { get; init; } = "";
            public string ExperienceLevel { get; init; } = "";
            public string RegistrationDate { get; init; } = "";
            public bool IsActive { get; init; } = true;
        }
    }
}