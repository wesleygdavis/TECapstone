﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Capstone.Models
{
    public class Volume
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string ApiDetailUrl { get; set; }
        public string SiteDetailUrl { get; set; }
    }
}
