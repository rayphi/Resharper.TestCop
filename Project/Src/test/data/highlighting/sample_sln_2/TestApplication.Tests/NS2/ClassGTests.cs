using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCop.TestApplication2;

namespace TestCop.TestApplication2Tests.NS2
{
    [TestClass]
    public class ClassGTests
    {
        /* test to confirm that testcop offers the best class first when more than one class is called 'ClassG' 
           We should be offered the NS2 namespaced version first... */
        [TestMethod]
        public void ReturnsTrueMethodTest()
        {            
        }
    }
}
