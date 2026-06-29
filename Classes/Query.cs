using MailDataImporter.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace MailDataImporter.Classes;

public class Query: IDisposable
{
    private const decimal TrsMpaToPsi = 145.038m;
    private SqlConnectionStringBuilder _builder;
    private readonly string _connectionString;
    private SqlConnection _sqlConnection;

    /// <summary>
    /// Initialize the Query class and open a SQL connection
    /// </summary>
    /// <param name="config">The application configuration to pull connection string properties from</param>
    public Query(AppConfig config)
    {
        _builder = new SqlConnectionStringBuilder
        {
            DataSource = config.Sql.DataSource,
            UserID = config.Sql.Username,
            Password = config.Sql.Password,
            InitialCatalog = config.Sql.InitialCatalog,
            IntegratedSecurity = false,
            Encrypt = false,
            TrustServerCertificate = true
        };
        
        _connectionString = _builder.ConnectionString;
        _sqlConnection = new SqlConnection( _connectionString);
        _sqlConnection.Open();
    }

    /// <summary>
    /// Cleanly close the SQL connection
    /// </summary>
    public void Dispose()
    {
        _sqlConnection.Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Get the magnetic saturation correction factor from SQL database to use when processing values reported on supplier test reports
    /// </summary>
    /// <param name="grade">The grade to search for</param>
    /// <param name="supplier">The supplier name to use for determining which version of the grade specification to use</param>
    /// <param name="logger">The logging interface for relaying messages back to the system</param>
    /// <returns>The correction factor for the grade and supplier combination. Defaults to returning 1.000</returns>
    private decimal GetMagSatCorrectionFactor(string grade, string supplier, ILogger logger)
    {
        decimal conversionFactor = 0m;
        string supplierName = "";
        
        // Assign supplierName based on special rules of which grades have different specifications by Supplier
            // Redacted if...else block
        
        // Coalesce value to return 1 if value in database is NULL
        // No single quotes for {grade} as they are added in to the string in the calling method
        string sqlString = $"SELECT COALESCE([adjustment_factor],1.000) FROM [redacted] WHERE grade = {grade} AND supplier = '{supplierName}';";
        bool hasRows = false;

        try
        {
            using var command = new SqlCommand(sqlString, _sqlConnection);
            var results = command.ExecuteReader();
            hasRows = results.HasRows;
            
            while (results.Read())
            {
                conversionFactor = results.GetDecimal(0);
            }

            results.Close();
        }
        catch (SqlException e)
        {
            logger.LogError(e.Message);
            conversionFactor = 1.000m;
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
            conversionFactor = 1.000m;
        }
        
        if (hasRows == false)
        {
            logger.LogError($"There was no match for grade {grade} and '{supplierName}'.");
            throw new InvalidDataException(
                "No magnetic saturation correction factor exists for the grade and supplier combination.");
        }
        
        return conversionFactor;
    }

    /// <summary>
    /// Inserts test report values into the database.
    /// </summary>
    /// <param name="data">The list of TestReport objects to import</param>
    /// <param name="logger">The logging interface for relaying messages back to the system</param>
    public int InsertTestReportData(List<TestReport> data, ILogger logger)
    {
        string sqlString = "INSERT INTO [redacted]\n(Grade, LotNo, BlankLotNo, RTPLotNo, Commodity, Quantity, Hardness, Density, Hc, 4ps, TRS, TRSPSI, Porosity, GrainSize, Pitch, ShipDate, ShipMethod, Supplier_Name, txt4TTQTMCert)\nVALUES";
        int i = 0;

        foreach (TestReport line in data)
        {
            if (i > 0)
                sqlString += "\n,";
            else
                sqlString += "\n";

            string grade = line.Grade == null ? "NULL" : $"'{line.Grade}'";
            string lot = line.LotNumber == null ? "NULL" : $"'{line.LotNumber}'";
            string blankLot = line.BlankLotNumber == null ? "NULL" : $"'{line.BlankLotNumber}'";
            string rtpLot = line.RtpLotNumber == null ? "NULL" : $"'{line.RtpLotNumber}'";
            string partNumber = line.PartNumber == null ? "NULL" : $"'{line.PartNumber}'";
            int coercivity = line.Coercivity == null ? 0 : line.Coercivity.Value;
            int magSat = line.MagneticSaturation == null ? 0 : line.MagneticSaturation.Value;
            
            int convertedMagSat = Convert.ToInt32(magSat * GetMagSatCorrectionFactor(grade, line.Supplier, logger));
            // Redacted logic for handling special processing rules based on supplier name
                // convertedMagSat = magSat
            
            float trs = line.TransverseRuptureStrength == null ? 0 : line.TransverseRuptureStrength.Value;
            decimal trsPsi = Math.Round((decimal)trs * TrsMpaToPsi);
            string porosity =  line.Porosity == null ? "NULL" : $"'{line.Porosity}'";
            string grainSize = line.GrainSize == null ? "NULL" : $"'{line.GrainSize}'";
            string pitch = line.Pitch == null ? "NULL" : $"'{line.Pitch}'";
            
            string shipMethod = "";
            if (line.ShipMethod!.Contains("Ocean", StringComparison.OrdinalIgnoreCase) || line.ShipMethod!.Contains("Sea", StringComparison.OrdinalIgnoreCase))
            {
                shipMethod = "'Ocean'";
            }
            else if (line.ShipMethod!.Contains("Air", StringComparison.OrdinalIgnoreCase))
            {
                shipMethod = "'Air'";
            }
            else
            {
                shipMethod = "'Other'";
            }
            
            sqlString += $"({grade},{lot},{blankLot},{rtpLot},{partNumber},{line.Quantity},{line.Hardness},{line.Density},{coercivity},{magSat},{trs},{trsPsi},{porosity},{grainSize},{pitch},'{line.ShipDate}',{shipMethod},'{line.Supplier}', {convertedMagSat})";
            i++;
        }

        sqlString += ";";
        
        try
        {
            using var command = new SqlCommand(sqlString, _sqlConnection);
            var affected = command.ExecuteNonQuery();

            return affected;
        }
        catch (SqlException e)
        {
            logger.LogError(e.Message);
            return 0;
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
            return 0;
        }
    }
}