using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class CurrencyConversionFunction
{
    private readonly string _connectionString;
    
    public CurrencyConversionFunction()
    {
        _connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
    }
    [FunctionName("CurrencyConversionFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        string connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");

        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            int id = data?.Id;

            if (id == 0)
            {
                return new BadRequestObjectResult("Invalid request. Please provide the 'Id' and 'Currency' properties.");
            }


            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Read the current currency value from the database
                string selectQuery = "SELECT Amount, SWIFTCode FROM Transactions WHERE TransactionId = @id";
                var selectCommand = new Microsoft.Data.SqlClient.SqlCommand(selectQuery, connection);
                selectCommand.Parameters.AddWithValue("@id", id);
                SqlDataReader reader = await selectCommand.ExecuteReaderAsync();

                reader.Read();
                double currentAmount = Convert.ToDouble(reader["Amount"]);
                string currencyType = reader["SWIFTCode"].ToString();
                connection.Close();

                string targetCurrency = currencyType.ToUpper() == "EUR" ? "USD" : "EUR";

                // Conversion rates based on the currency types
                double conversionRate = currencyType.ToUpper() == "EUR" ? 1.12 : 0.89;

                double convertedAmount = currentAmount * conversionRate;



                // Update the currency value and type in the database
                await connection.OpenAsync();
                string updateQuery = @"UPDATE Transactions SET Amount = @convertedAmount, SWIFTCode = @targetCurrency WHERE TransactionId = @id";
                using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@convertedAmount", convertedAmount);
                updateCommand.Parameters.AddWithValue("@targetCurrency", targetCurrency);
                updateCommand.Parameters.AddWithValue("@id", id);

                updateCommand.ExecuteNonQuery();
                connection.Close();

                var response = new
                {
                    Id = id,
                    Currency = targetCurrency,
                    PreviousAmount = currentAmount,
                    ConvertedAmount = convertedAmount
                };


                return new OkObjectResult(response);
                

            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "An error occurred during currency conversion");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}


