using System;

namespace PreLedgerORC.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}