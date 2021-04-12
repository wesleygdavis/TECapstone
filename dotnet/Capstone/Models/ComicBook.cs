﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Capstone.Models
{
    public class ComicBook
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string IssueNumber { get; set; }
        public string CoverDate { get; set; }
        public string SiteDetailUrl { get; set; }
        public ComicImages Image { get; set; }
    }
}
