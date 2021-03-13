﻿namespace Yandex.Music.Api.Models.Common
{
    public class YSubscription
    {
        public bool CanStartTrial { get; set; }
        public bool HadAnySubscription { get; set; }
        public bool McDonalds { get; set; }
        public YPeriod NonAutoRenewable { get; set; }
    }
}