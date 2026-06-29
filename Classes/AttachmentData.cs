using System.Text;
using MailDataImporter.Models;
using Microsoft.Extensions.Logging;

namespace MailDataImporter.Classes;

public static class AttachmentData
{
    /// <summary>
    /// A helper function to set a string value to null if it is empty
    /// </summary>
    /// <param name="value">The nullable string value to check</param>
    /// <returns>null if empty or the value passed in</returns>
    private static string? NullIfEmptyString(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    
    /// <summary>
    /// A helper function to set an integer value to null if it is empty
    /// </summary>
    /// <param name="value">The nullable string value to check</param>
    /// <returns>null if empty or the value passed in converted to an Int32</returns>
    private static int? NullIfEmptyInt(string? value) => string.IsNullOrWhiteSpace(value) ? null : Convert.ToInt32(Math.Round(Convert.ToDecimal(value)));
    
    /// <summary>
    /// A helper function to set a float value to null if it is empty
    /// </summary>
    /// <param name="value">The nullable string value to check</param>
    /// <returns>null if empty or the value passed in converted to a Float</returns>
    private static float? NullIfEmptyFloat(string? value) => string.IsNullOrWhiteSpace(value) ? null : (float)Convert.ToDecimal(value);

    /// <summary>
    /// Processes test report byte arrays from attachments and assigns the values to properties of the TestReport object.
    /// </summary>
    /// <param name="attachments">The list of test report attachments to process</param>
    /// <param name="logger">The system logging interface to write log messages back to the system.</param>
    /// <param name="exchangeMail">The ExchangeMail object to process sending emails</param>
    /// <returns>A list of TestReport objects</returns>
    /// <exception cref="InvalidDataException">Thrown to indicate that the format of the text file is not in the expected format</exception>
    public static async Task<List<TestReport>> ProcessTestReports(List<Attachment> attachments, ILogger logger, ExchangeMail exchangeMail)
    {
        List<TestReport> testReportData = new List<TestReport>();
        
        foreach (var attachment in attachments)
        {
            UTF8Encoding utf8 = new UTF8Encoding();
            
            if (attachment.Content == null || attachment.Content.Length == 0)
            {
                logger.LogError($"The attachment {attachment.Name} is empty.");
                RaiseTestReportError(attachment.Name, exchangeMail);
            }
            
            string content = utf8.GetString(attachment.Content).Trim();
            string[] lines = content.Split('\n');
            
            string supplierName;
            
            // Determine supplier based on email subject.
            // Supplier name parsing if-else logic redacted 
            
            // Process each line in attachment and build a test report data list
            for (int i = 0; i < lines.Length; i++)
            {
                if (attachment.EmailSubject.Contains("[redacted]", StringComparison.OrdinalIgnoreCase) || supplierName == "[redacted]")
                {
                    if (i > 0)
                    {
                        var data = lines[i].Trim().Split('\t');
                        try
                        {
                            testReportData.Add(
                                new TestReport(
                                    NullIfEmptyString(data[0]),
                                    NullIfEmptyString(data[1]),
                                    NullIfEmptyString(data[2]),
                                    NullIfEmptyString(data[3]),
                                    NullIfEmptyString(data[4]),
                                    Convert.ToInt32(data[5]),
                                    Convert.ToDecimal(data[6]),
                                    Convert.ToDecimal(data[7]),
                                    NullIfEmptyInt(data[8]),
                                    NullIfEmptyInt(data[9]),
                                    NullIfEmptyFloat(data[10]),
                                    NullIfEmptyString(data[11]),
                                    Convert.ToDecimal(data[12]),
                                    NullIfEmptyString(data[13]),
                                    DateOnly.FromDateTime(Convert.ToDateTime(data[14])),
                                    NullIfEmptyString(data[15]),
                                    supplierName
                                )
                            );
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e.Message);
                            RaiseTestReportError(attachment.Name, exchangeMail);
                        }
                    }
                }
                else
                {
                    if (i < 4)
                    {
                        continue;
                    }
                    
                    var data = lines[i].Trim().Split('\t');
                    try
                    {
                        testReportData.Add(
                            new TestReport(
                                NullIfEmptyString(data[0]),
                                NullIfEmptyString(data[1]),
                                NullIfEmptyString(data[2]),
                                NullIfEmptyString(data[3]),
                                NullIfEmptyString(data[4]),
                                Convert.ToInt32(data[5]),
                                Convert.ToDecimal(data[6]),
                                Convert.ToDecimal(data[7]),
                                NullIfEmptyInt(data[8]),
                                NullIfEmptyInt(data[9]),
                                NullIfEmptyFloat(data[10]),
                                NullIfEmptyString(data[11]),
                                Convert.ToDecimal(data[12]),
                                NullIfEmptyString(data[13]),
                                DateOnly.FromDateTime(Convert.ToDateTime(data[14])),
                                NullIfEmptyString(data[15]),
                                supplierName
                            )
                        );
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e.Message);
                        RaiseTestReportError(attachment.Name, exchangeMail);
                    }
                }
            }
        }

        // Aggregate the data to prevent inserting duplicate key values into the database
        var aggregatedData = testReportData
            .GroupBy(row => new
            {
                row.Grade, row.LotNumber, row.BlankLotNumber, row.RtpLotNumber, row.PartNumber, row.ShipDate,
                row.ShipMethod, row.Supplier
            })
            .Select(group => new TestReport(
                    grade: group.Key.Grade,
                    lot: group.Key.LotNumber,
                    blankLot: group.Key.BlankLotNumber,
                    rtpLot: group.Key.RtpLotNumber,
                    partNumber: group.Key.PartNumber,
                    qty: group.Sum(row => row.Quantity),
                    hardness: group.Average(row => row.Hardness),
                    density: group.Average(row => row.Density),
                    coercivity: group.Average(row => row.Coercivity) is double c ? (int?)Math.Round(c) : null,
                    magneticSaturation: group.Average(row => row.MagneticSaturation) is double m
                        ? (int?)Math.Round(m)
                        : null,
                    trs: group.Average(row => row.TransverseRuptureStrength) is float t ? t : null,
                    porosity: group.First().Porosity,
                    grainSize: group.Average(row => row.GrainSize),
                    pitch: group.First().Pitch,
                    shipDate: group.Key.ShipDate,
                    shipMethod: group.Key.ShipMethod,
                    supplier: group.Key.Supplier
                )
            ).ToList();
        
        return aggregatedData;
    }

    private static async void RaiseTestReportError(string reportName, ExchangeMail exchangeMail)
    {
        var errorMessage = $"The test report {reportName} is not in the proper format to be imported.";
        await exchangeMail.SendMailGenericAsync(errorMessage);
        throw new InvalidDataException(errorMessage);
    }
}