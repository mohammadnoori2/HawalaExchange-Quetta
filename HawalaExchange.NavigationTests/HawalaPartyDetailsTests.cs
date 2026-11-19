using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Components.Shared.Modals;
using Xunit;

namespace HawalaExchange.NavigationTests;
public class HawalaPartyDetailsTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    [Theory]
    [InlineData("SenderFatherName")]
    [InlineData("SenderPhone")]
    [InlineData("SenderTazkiraNumber")]
    [InlineData("SenderTazkiraImagePath")]
    [InlineData("SenderAddress")]
    [InlineData("ReceiverFatherName")]
    [InlineData("ReceiverPhone")]
    [InlineData("ReceiverTazkiraNumber")]
    [InlineData("ReceiverTazkiraImagePath")]
    [InlineData("ReceiverAddress")]
    public void Editing_opens_shared_details_when_either_party_has_supplemental_data(string property)
    {
        var modal=new CreateHawalaModal {IsEditMode=true};
        var model=new CreateHawalaDto {SenderName="Sender",ReceiverName="Receiver"};
        typeof(CreateHawalaDto).GetProperty(property)!.SetValue(model,"stored value");
        typeof(CreateHawalaModal).GetProperty("CurrentHawala",Hidden)!.SetValue(modal,model);
        typeof(CreateHawalaModal).GetMethod("InitializePartyDetails",Hidden)!.Invoke(modal,null);
        Assert.True(Expanded(modal,"showPartyDetails"));
    }
    [Fact]
    public void Names_and_whitespace_alone_do_not_expand_details_and_new_forms_reset_toggles()
    {
        var modal=new CreateHawalaModal {IsEditMode=true};
        var model=new CreateHawalaDto {SenderName="Sender",ReceiverName="Receiver",SenderPhone=" ",ReceiverAddress="\t"};
        typeof(CreateHawalaModal).GetProperty("CurrentHawala",Hidden)!.SetValue(modal,model);
        typeof(CreateHawalaModal).GetMethod("InitializePartyDetails",Hidden)!.Invoke(modal,null);
        Assert.False(Expanded(modal,"showPartyDetails"));
        typeof(CreateHawalaModal).GetField("showPartyDetails",Hidden)!.SetValue(modal,true);
        typeof(CreateHawalaModal).GetMethod("ResetHawala",Hidden)!.Invoke(modal,null);
        Assert.False(Expanded(modal,"showPartyDetails"));
    }
    static bool Expanded(CreateHawalaModal modal,string name)=>(bool)typeof(CreateHawalaModal).GetField(name,Hidden)!.GetValue(modal)!;
}
