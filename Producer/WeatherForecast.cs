namespace Producer;

public class WeatherForecast
{
    public DateOnly Date { get; set; }

    public int TemperatureC { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC * 9.0 / 5);

    public string? Summary { get; set; }
}
