using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Columns;

public class ReorderColumnsRequest
{
    [Required]
    public List<int> ColumnIds { get; set; } = [];
}
