using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;
#if !Net
using Microsoft.Extensions.Configuration;
#endif
using Serilog;

namespace DataDump
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
#if Net
                .ReadFrom.AppSettings()
#else
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile(
                        path: "appsettings.json",
                        optional: false,
                        reloadOnChange: true)
                    .Build())
#endif
                .CreateLogger();

            Log.Information("Program start");

            var exitCode = 0;
            var cmd = ConfigureCommandLine();
            try
            {
                exitCode = cmd.Execute(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                exitCode = -1;
            }

            Environment.ExitCode = exitCode;

            Log.Information("Exit with code: {0}", exitCode);
        }

        private static CommandLineApplication ConfigureCommandLine()
        {
            var cmd = new CommandLineApplication(false);
            cmd.HelpOption("-h");

            var databaseTypeOption = cmd.Option(
                "--database-type|-t",
                "Specific database type: " + DatabaseTypeDescriptionString(),
                CommandOptionType.SingleValue);
            
            var connectionStringOption = cmd.Option(
                "--connection-string|-c",
                "Database connection string",
                CommandOptionType.SingleValue);

            var inputOption = cmd.Option(
                "--input|-i",
                "Input SQL query file",
                CommandOptionType.SingleValue);
            
            var outputOption = cmd.Option(
                "--output|-o",
                "Path to out put file",
                CommandOptionType.SingleValue);

            cmd.OnExecute(() =>
            {
                if (!databaseTypeOption.HasValue() ||
                    !connectionStringOption.HasValue() ||
                    !inputOption.HasValue() ||
                    !outputOption.HasValue())
                {
                    var message = "One or more parameters are incorrect or missing";
                    Log.Logger.Error(message);
                    Console.Error.WriteLine(message);
                    cmd.ShowHelp();
                    return -1;
                }

                var connectionString = connectionStringOption.Value();

                DatabaseType databaseType;
                if (!IsValidDatabaseType(databaseTypeOption.Value(), out databaseType))
                {
                    var message = "Unsupported database type: " + databaseType;
                    Log.Logger.Error(message);
                    Console.Error.WriteLine(message);
                    cmd.ShowHelp();
                    return -2;
                }

                var input = inputOption.Value();
                if (!File.Exists(input))
                {
                    var message = "Cannot open file " + input;
                    Log.Logger.Error(message);
                    Console.Error.WriteLine(message);
                    cmd.ShowHelp();
                    return -2;
                }

                var output = outputOption.Value();

                WriteData(databaseType, connectionString, input, output);

                return 0;
            });
            
            return cmd;
        }

        private static string DatabaseTypeDescriptionString()
        {
            var names = Enum.GetNames(typeof(DatabaseType));

            return string.Join(", ", names, 0, names.Length - 1) + " or " + names[names.Length - 1];
        }

        private static bool IsValidDatabaseType(string databaseType, out DatabaseType value)
        {
            return Enum.TryParse(databaseType, true, out value);
        }

        private static System.Data.Common.DbConnection OpenConnection(DatabaseType databaseType, string connectionString)
        {
            var collection = new Dictionary<DatabaseType, string>
            {
                [DatabaseType.MSSQL]    = "System.Data.SqlClient",
#if Net
                [DatabaseType.Oracle]   = "Oracle.ManagedDataAccess.Client"
#endif
            };

#if Net
            var conn = System.Data.Common.DbProviderFactories
                .GetFactory(collection[databaseType])
                .CreateConnection();
            conn.ConnectionString = connectionString;
#else
            System.Data.Common.DbConnection conn = null;
            switch (databaseType)
            {
                case DatabaseType.MSSQL:
                    conn = new System.Data.SqlClient.SqlConnection();
                    break;
            };
#endif
            Log.Information("Establish connection to database");
            conn.Open();
            return conn;
        }

        private static void WriteData(DatabaseType databaseType, string connectionString, string input, string output)
        {
            Log.Information("Read file {0}", input);
            var query = string.Empty;
            using (var reader = new StreamReader(input))
            {
                query = reader.ReadToEnd();
            }

            var conn = OpenConnection(databaseType, connectionString);

            using (var cmd = conn.CreateCommand())
            {
                Log.Information("Execute query");
                cmd.CommandText = query;

                using (var reader = cmd.ExecuteReader())
                using (var writer = new StreamWriter(output, false))
                {
                    Log.Information("Write file {0}", output);
                    WriteCSV(reader, writer);
                }
            }
        }

        private static void WriteCSV(System.Data.Common.DbDataReader reader, TextWriter writer)
        {
            Action<string> logBadData = s =>
            {
                Log.Logger.Warning(s);
                Console.Error.WriteLine("Unable to write record: {0}", s);
            };
            
            using (var csv = new CsvHelper.CsvWriter(writer))
            {
                csv.Configuration.TrimOptions = CsvHelper.Configuration.TrimOptions.None;
                
                int length = reader.FieldCount;

                for (var i = 0; i < length; i++)
                {
                    csv.WriteField(reader.GetName(i));
                }
                csv.NextRecord();

                while (reader.Read())
                {
                    for (var i = 0; i < length; i++)
                    {
                        csv.WriteField(reader[i]);
                    }
                    csv.NextRecord();
                }
            }
        }
    }
}
