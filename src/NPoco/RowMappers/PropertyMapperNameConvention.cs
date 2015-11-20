using System;
using System.Collections.Generic;
using System.Linq;

namespace NPoco.RowMappers
{
    public static class PropertyMapperNameConvention
    {
        public static string SplitPrefix = "npoco_";

        public static IEnumerable<PosName> ConvertFromNewConvention(this IEnumerable<PosName> posNames)
        {
            string prefix = null;
            foreach (var posName in posNames)
            {
                if (posName.Name.StartsWith(SplitPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = posName.Name.Substring(SplitPrefix.Length);
                    continue;
                }

                if (prefix != null)
                {
                    posName.Name = PocoDataBuilder.JoinStrings(prefix, posName.Name);
                }

                yield return posName;
            }
        }

        public static IEnumerable<PosName> ConvertFromOldConvention(this IEnumerable<PosName> posNames, List<PocoMember> pocoMembers)
        {
            var used = new Dictionary<PocoMember, int>();
            var members = FlattenPocoMembers(pocoMembers, new LevelMonitor()).ToList();
            var level = 0;

            foreach (var posName in posNames)
            {
                var unusedPocoMembers = members.Where(x => !used.ContainsKey(x.PocoMember) && x.Level >= level)
                    .Select(x => x.PocoMember)
                    .ToList();

                var member = PropertyMapper.FindMember(unusedPocoMembers, posName.Name);
                if (member != null && member.PocoColumn != null)
                {
                    level = members.Single(x => x.PocoMember == member).Level;
                    used.Add(member, level);
                    posName.Name = member.PocoColumn.MemberInfoKey;
                }

                yield return posName;
            }
        }

        private static IEnumerable<PocoMemberLevel> FlattenPocoMembers(List<PocoMember> pocoMembers, LevelMonitor levelMonitor)
        {

            foreach (var pocoMember in pocoMembers.OrderBy(x => x.PocoMemberChildren.Count != 0))
            {
                if (pocoMember.PocoColumn != null)
                {
                    yield return new PocoMemberLevel { PocoMember = pocoMember, Level = levelMonitor.Level };
                }

                if (pocoMember.PocoMemberChildren.Count == 0)
                    continue;

                levelMonitor.Level++;
                foreach (var pocoMemberLevel in FlattenPocoMembers(pocoMember.PocoMemberChildren, levelMonitor))
                {
                    yield return pocoMemberLevel;
                }
            }
        }

        private struct PocoMemberLevel
        {
            public PocoMember PocoMember { get; set; }
            public int Level { get; set; }
        }

        private class LevelMonitor
        {
            public LevelMonitor()
            {
                Level = 1;
            }
            public int Level { get; set; }
        }
    }
}