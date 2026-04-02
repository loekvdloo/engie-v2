using System.Collections.Generic;

namespace Engie.Mca.EventHandler.Services;

public static class FaultCodeCatalog
{
    private static readonly Dictionary<string, string> ErrorCodes = new()
    {
        // XML/Technical Errors
        { "650", "Ongeldig XML-formaat" },
        { "651", "XML kan niet geparst worden" },
        { "652", "Vereist XML-element ontbreekt" },
        { "653", "Ongelijkmatige naamruimte in XML" },

        // Message Type Errors
        { "654", "Onbekend berichttype" },
        { "655", "Niet-ondersteund berichttype" },
        { "656", "Berichttype kan niet bepaald worden" },

        // Field Validation Errors
        { "676", "Vereist veld ontbreekt" },
        { "677", "Ongeldig veldformaat" },
        { "678", "Veld buiten bereik" },
        { "679", "Ongeldig datumformaat" },
        { "680", "Ongeldig numeriek formaat" },

        // Business Rule Errors
        { "683", "Ongeldige combinatie herkomstaanduiding, validatiestatus en herstelmethode" },
        { "686", "Ongeldige EAN-code" },
        { "687", "Ongeldige leverancierscode" },
        { "688", "Onbekende markt/segment" },
        { "689", "Ongeldig contract-ID" },

        // BRP Register Errors
        { "700", "BRP-register niet beschikbaar" },
        { "701", "Gegevens niet gevonden in BRP" },
        { "702", "BRP-register verouderd" },

        // Sequence/Order Errors
        { "754", "Ongeldige bericht-sequence" },
        { "755", "Dubbele bericht-ID gedetecteerd" },
        { "756", "Volgorde van velden onjuist" },

        // Time Window Errors
        { "758", "Bericht buiten geldige periode" },
        { "759", "Bericht te oud" },
        { "760", "Bericht in toekomst" },

        // Quantity/Amount Errors
        { "772", "Negatieve hoeveelheid niet toegestaan" },
        { "773", "Hoeveelheid overschrijdt limiet" },
        { "774", "Hoeveelheid nul niet toegestaan" },
        { "775", "Hoeveelheid ongeldig formaat" },

        // Configuration Errors
        { "780", "Verwerking niet geconfigureerd" },
        { "781", "Verwerking geblokkeerd" },
        { "782", "Configuratie niet geladen" },

        // Generic/System Errors
        { "999", "Onbekende fout bij verwerking" }
    };

    public static string GetDescription(string code)
    {
        return ErrorCodes.TryGetValue(code, out var description)
            ? description
            : "Onbekende foutcode";
    }

    public static List<string> AllCodes => new(ErrorCodes.Keys);
}
