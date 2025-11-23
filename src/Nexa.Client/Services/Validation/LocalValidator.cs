using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Nexa.Client.Services.Exceptions;
using Nexa.Shared.Models;
using System;

namespace Nexa.Client.Services.Validation
{
    public static class LocalValidator
    {
        /// <summary>
        /// Waliduje obiekt używając atrybutów DataAnnotations (takich samych jak na backendzie).
        /// Jeśli walidacja nie przejdzie, rzuca NexaClientException z kodem VALIDATION_ERROR.
        /// </summary>
        public static void Validate<T>(T model)
        {
            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();

            bool isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                model, context, results, validateAllProperties: true);

            if (!isValid)
            {
                var errorContext = new Dictionary<string, object>();

                foreach (var validationResult in results)
                {
                    // Kluczem jest nazwa pola (np. "Email"), wartością komunikat błędu
                    foreach (var memberName in validationResult.MemberNames)
                    {
                        errorContext[memberName] = validationResult.ErrorMessage ?? "Invalid value";
                    }
                }

                var errorResponse = new ErrorResponse
                {
                    ErrorCode = ErrorCode.VALIDATION_ERROR, 
                    Message = "Błąd walidacji formularza.",
                    Context = errorContext,
                    Timestamp = DateTime.UtcNow
                };

                throw new NexaClientException(errorResponse, 400);
            }
        }
    }
}