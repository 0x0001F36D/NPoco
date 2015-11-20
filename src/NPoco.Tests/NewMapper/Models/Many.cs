using NPoco;

namespace NPoco.Tests.NewMapper.Models
{
    [TableName("Manys"), PrimaryKey("ManyId")]
    public class Many
    {
        public int ManyId { get; set; }
        //[Reference(ReferenceType.Foreign, Name = "OneId", ReferenceMemberName = "Id")]
        //public One One { get; set; }
        public int OneId { get; set; }
        public int Value { get; set; }
        public string Currency { get; set; }
    }
}