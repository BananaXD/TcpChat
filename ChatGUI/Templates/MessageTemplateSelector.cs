using ChatMaui.ViewModels;

namespace ChatMaui.Templates;

public class MessageTemplateSelector : DataTemplateSelector {
    public DataTemplate TextMessageTemplate { get; set; } 
    public DataTemplate PhotoMessageTemplate { get; set; } 
    public DataTemplate FileMessageTemplate { get; set; } 

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) {
        return item switch {
            TextMessageViewModel => TextMessageTemplate,
            PhotoMessageViewModel => PhotoMessageTemplate,
            FileMessageViewModel => FileMessageTemplate,
            _ => TextMessageTemplate
        };
    }
}