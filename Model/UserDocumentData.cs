using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Telegram_Task_Bot.Model
{
    class UserDocumentData
    {
        public string Surname { get; set; }
        public string GivenName { get; set; }
        public string PassportIdNumber { get; set; }
        public string DriversLicenseIdNumber { get; set; }
        public string BirthDate { get; set; }
        public string ExpiryDate { get; set; }
        public string Country { get; set; }
        public string Gender { get; set; }
        public string BirthPlace { get; set; }
    }
}
