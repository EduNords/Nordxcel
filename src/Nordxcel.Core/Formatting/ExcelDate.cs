namespace Nordxcel.Core.Formatting;

/// <summary>
/// Conversão entre o número serial de data do Excel e <see cref="DateTime"/>.
/// <para>
/// O Excel conta os dias a partir de 1900 e carrega até hoje um bug do Lotus 1-2-3:
/// ele acredita que 1900 foi bissexto, então o serial 60 corresponde a um
/// 29/02/1900 que nunca existiu. Datas a partir de 01/03/1900 ficam deslocadas em
/// um dia por causa disso, e a conversão precisa reproduzir o deslocamento para
/// que uma planilha trocada com o Excel mostre o mesmo dia.
/// </para>
/// </summary>
public static class ExcelDate
{
    /// <summary>Serial do dia inexistente 29/02/1900.</summary>
    private const int PhantomLeapDaySerial = 60;

    private static readonly DateTime EpochBeforePhantom = new(1899, 12, 31);
    private static readonly DateTime EpochAfterPhantom = new(1899, 12, 30);

    /// <summary>Último serial representável, véspera do ano 10000.</summary>
    public const double MaxSerial = 2_958_465.9999999d;

    public static bool TryFromSerial(double serial, out DateTime date)
    {
        date = default;

        if (!double.IsFinite(serial) || serial < 0d || serial > MaxSerial)
        {
            return false;
        }

        double days = Math.Floor(serial);
        double fraction = serial - days;

        DateTime origin = days < PhantomLeapDaySerial ? EpochBeforePhantom : EpochAfterPhantom;

        // O serial 60 é o 29/02/1900 fantasma; o Excel o exibe como tal, mas aqui
        // ele cai em 28/02/1900, que é o dia real correspondente.
        date = origin.AddDays(days).AddSeconds(Math.Round(fraction * 86_400d));

        return true;
    }

    public static double ToSerial(DateTime date)
    {
        DateTime day = date.Date;

        double serial = day < new DateTime(1900, 3, 1)
            ? (day - EpochBeforePhantom).TotalDays
            : (day - EpochAfterPhantom).TotalDays;

        return serial + date.TimeOfDay.TotalDays;
    }
}
