using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCop.TestApplication.NS1;

namespace TestCop.TestApplication.Tests
{
    /* USE CASE: 
     * TestCop does not enforce rules on abstract test classes  
     */
    [TestClass]
    abstract public class AbstractTestClass
    {      
        public void SomeHelperMethod()
        {
            
        }
    }
}