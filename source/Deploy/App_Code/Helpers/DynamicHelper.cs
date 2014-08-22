using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Dynamic;
using System.ComponentModel;

namespace Deploy.Helpers
{
    public class DynamicHelper
    {
        public static dynamic ToDynamic(object value)
        {
            IDictionary<string, object> expandoObject = new ExpandoObject();
            foreach (var property in value.GetType().GetProperties())
            {
                if (property.PropertyType.IsPublic)
                {
                    expandoObject[property.Name] = property.GetValue(value);
                }
                else
                {
                    expandoObject[property.Name] = ToDynamic(property.GetValue(value));
                }
            }
            return expandoObject;
        }

    }
}