using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace User_Analytics.Models
{
    public class MongoDatabaseService
    {
        private readonly IMongoCollection<UserModel> _usersCollection;//Filed name, direct line to the database, can be used whenever we need to access the data,(IMongoCollection) a generic interface from the MongoDB driver. IMongoCollection<T> represents a collection (table-like concept) of documents mapped to the type
        private readonly Random _rnd = new Random();// random number generator, creates a random object and stores it's value in _rnd variable

        public List<UserModel> CachedUsers { get; private set; } = new();//temperorily stores the data for resuibility
        public MongoDatabaseService(string connectionString, string dbName, string usersCollectionName)//Parameters 
        {
            var client = new MongoClient(connectionString);//Connects to MongoDB using your connection string
            var database = client.GetDatabase(dbName);
            _usersCollection = database.GetCollection<UserModel>(usersCollectionName);//Prepares the “Users” collection so you can run queries (like fetch, insert, count, etc.).
        }

        // Get all users
        public async Task<List<UserModel>> GetAllUsersAsync()//Keeps the UI responsive 
        {
            var users = await _usersCollection.Find(_ => true).ToListAsync();//includes all users by using Find everything (True), and converts it to the list
            CachedUsers = users; // cache users for ViewModel
            return users;
        }

        // Get active users
        public async Task<int> GetActiveUsersAsync()//An integer wrapped inside the task
        {
            return (int)await _usersCollection.CountDocumentsAsync(u => u.IsActive);//Convert it to int ,from the _usercollection count and filter all the IsActive users)
        }

        // Insert user
        public async Task InsertUserAsync(string name, string department, string experienceLevel, DateTime registrationDate, bool isActive = true)
        {
            var user = new UserModel
            {
                Name = name,
                Department = department,
                ExperienceLevel = experienceLevel,
                RegistrationDate = registrationDate,
                IsActive = isActive
            };
            await _usersCollection.InsertOneAsync(user);
        }
        public async Task ClearAllUsersAsync()
        {
            await _usersCollection.DeleteManyAsync(_ => true);//clears from the database
            CachedUsers.Clear();//also clears temperorily in memory user data
        }

        // Random user generator (for live demo charts)(arrays)
        private readonly string[] _departments = { "Software Engineer", "Firmware Engineer", "Mechanical Engineer", "IT", "Marketing", "HR", "Management" };
        private readonly string[] _experienceLevels = { "Junior", "Mid", "Senior" };

        public async Task InsertRandomUserAsync()
        {
            var name = $"User{_rnd.Next(1000, 9999)}";
            var department = _departments[_rnd.Next(_departments.Length)];//generates a random number from 0 to 6 and assigns the number value to a dept
            var experience = _experienceLevels[_rnd.Next(_experienceLevels.Length)];
            int year = _rnd.Next(2018, 2026); // 2019–2025 Random year
            int month = _rnd.Next(1, 13);//jan to dec Random months
            int day = _rnd.Next(1, DateTime.DaysInMonth(year, month) + 1);
            var registrationDate = new DateTime(year, month, day);

            await InsertUserAsync(name, department, experience, registrationDate, true);
        }

        public async Task InsertRandomUserForDepartmentAsync(string department)//Flexibility for generating users for a specific department
        {
            var name = $"User{_rnd.Next(1000, 9999)}";
            var experience = _experienceLevels[_rnd.Next(_experienceLevels.Length)];
            int year = _rnd.Next(2018, 2026); // 2019–2025 ❌ Random year
            int month = _rnd.Next(1, 13);
            int day = _rnd.Next(1, DateTime.DaysInMonth(year, month) + 1);
            var registrationDate = new DateTime(year, month, day);

            await InsertUserAsync(name, department, experience, registrationDate, true);
        } // Insert user for specific department 

        // User model
        public class UserModel
        {
            public string Id { get; set; } = ObjectId.GenerateNewId().ToString();//12-byte unique identifier generated in BSon , then converted to string, Each property represents a field in the user document. 
            public string Name { get; set; } = "";
            public string Department { get; set; } = "";
            public string ExperienceLevel { get; set; } = "";
            public DateTime RegistrationDate { get; set; } = DateTime.Now;
            public bool IsActive { get; set; } = true;
        }
    }
}
